#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AppendDateTime.IPerf3;
using AppendDateTime.SpeedTest;
using JPB.Console.Helper.Grid.CommandDispatcher;
using JPB.Console.Helper.Grid.Grid;

#endregion

namespace AppendDateTime
{
	public class Program
	{
		private readonly Regex _pingFailRegex;

		private readonly Regex _pingOkRegex;
		private readonly ConsoleCommandDispatcher _cmdDispatcher;
		private readonly string _file;

		private decimal _lowerNetworkBound;
		private bool _stopRequested;

		public Program()
		{
			SpeedProcessors = new List<ISpeedTestProcessor>();
			_pingOkRegex = new Regex(@"(?:Ping result (\w*) time=(\d*))+");
			_pingFailRegex = new Regex(@"Error");
			FromMinutesSlowNetwork = TimeSpan.FromSeconds(10);
			FasterLookup = true;
			State = new DataInfo {Name = nameof(State)};
			TimeToNext = new DataInfo {Name = nameof(TimeToNext)};
			LastNetworkSend = new DataInfo(false) {Name = nameof(LastNetworkSend)};
			LastNetworkRecive = new DataInfo(false) {Name = nameof(LastNetworkRecive)};
			AverageNetwork = new DataInfo(false) {Name = nameof(AverageNetwork)};
			LowNetwork = new DataInfo(false) {Name = nameof(LowNetwork)};
			MaxNetwork = new DataInfo(false) {Name = nameof(MaxNetwork)};
			MinNetwork = new DataInfo(false) {Name = nameof(MinNetwork)};
			MeasurementsCount = new DataInfo {Name = nameof(MeasurementsCount)};
			PingAverage = new DataInfo {Name = nameof(PingAverage)};
			PingsCount = new DataInfo {Name = nameof(PingsCount)};
			PacketLoss = new DataInfo {Name = nameof(PacketLoss) + " 10 Minutes"};
			PacketLossCurrently = new DataInfo {Name = nameof(PacketLossCurrently) + " Last 100"};
			Imported = new DataInfo {Name = nameof(Imported)};
			FileCache = new DataInfo {Name = nameof(FileCache)};
			PingDestinations = new[]
			{
				new PingInfo("google.de"),
				new PingInfo("google-public-dns-b.google.com"),
				new PingInfo("Cloudflare.com")
			};
			Output = new List<string>();

			Console.WriteLine("Type H for help");
			SteamBufferSize = 6142;
			FromMinutes = TimeSpan.FromMinutes(1);
			CurrentWaitTime = FromMinutesSlowNetwork;
			StatisticsDown = new ConcurrentBag<DataPoint>();
			StatisticsUp = new ConcurrentBag<DataPoint>();
			Pings = new ConcurrentBag<DataPoint>();
			TextGrid = new TextGrid<DataInfo>(ColumnGenerationMode.NoColumns, false);
			TextGrid.Columns.Add(new BoundConsoleGridColumn(nameof(DataInfo.Name)));
			TextGrid.Columns.Add(new BoundConsoleGridColumn(nameof(DataInfo.Value)));
			TextGrid.Columns.Add(new BoundConsoleGridColumn(nameof(DataInfo.Tendecy)));
			for (var i = 1; i < 4; i++)
			{
				var i1 = i;
				var historyColumnName = "History #" + i;
				TextGrid.Columns.Add(new ConsoleGridColumn(historyColumnName)
				{
					GetValue = f => (f as DataInfo).History.ElementAtOrDefault(i1)
				});

				TextGrid.ConsolePropertyGridStyle.ColumnStyles.Add(historyColumnName, new ConsoleColumnStyle
				{
					ConsoleCellStyle = new DelegateConsoleCellStyle
					{
						Background = dataPoint =>
						{
							var dp = dataPoint as DataInfo;
							if (!DataInfo.ParseValue(dp.Value, out var val))
							{
								return null;
							}

							var previusValue = dp.History.ElementAtOrDefault(i1 - 1);

							if (previusValue == val)
							{
								return null;
							}

							if (previusValue < val)
							{
								return dp.SmallerBetter ? ConsoleColor.Red : ConsoleColor.Green;
							}

							if (previusValue > val)
							{
								return dp.SmallerBetter ? ConsoleColor.Green : ConsoleColor.Red;
							}

							return (ConsoleColor?) null;
						}
					}
				});
			}

			TextGrid.ConsolePropertyGridStyle.ColumnStyles.Add(nameof(DataInfo.Tendecy), new ConsoleColumnStyle
			{
				ConsoleCellStyle = new DelegateConsoleCellStyle
				{
					Background = dataPoint =>
					{
						var dp = dataPoint as DataInfo;
						if (dp.Tendecy == "+")
						{
							return dp.SmallerBetter ? ConsoleColor.Red : ConsoleColor.Green;
						}

						if (dp.Tendecy == "-")
						{
							return dp.SmallerBetter ? ConsoleColor.Green : ConsoleColor.Red;
						}

						return (ConsoleColor?) null;
					}
				}
			});

			TextGrid.ConsolePropertyGridStyle.AlternatingTextStyle = ConsoleColor.Black;
			TextGrid.ConsolePropertyGridStyle.AlternatingTextBackgroundStyle = ConsoleColor.White;
			TextGrid.CenterText = true;
			TextGrid.ClearConsole = true;
			TextGrid.ExpandConsole = true;
			TextGrid.ObserveList = false;
			TextGrid.PersistendAdditionalInfos = true;
			TextGrid.ExtraInfos = new StringBuilder();

			foreach (var consoleGridColumn in TextGrid.Columns)
			{
				consoleGridColumn.CacheStrategy = CacheStrategy.Never;
			}

			TextGrid.SourceList = new ObservableCollection<DataInfo>(Yield());

			_file = Environment.ExpandEnvironmentVariables(Environment.GetCommandLineArgs().Skip(1).First());

			if (!Directory.Exists(Path.GetDirectoryName(_file)))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_file));
			}

			if (!File.Exists(_file))
			{
				File.Create(_file).Dispose();
			}

			if (Environment.GetCommandLineArgs().Contains("-i"))
			{
				var statistics = File.ReadAllLines(_file);

				ImportStatistics(statistics);
				Set(Imported, StatisticsDown.Count.ToString());
				if (StatisticsDown.Count > 0)
				{
					Set(MaxNetwork, StatisticsDown.Max(f => f.Value) + "mbit/s");
					Set(MinNetwork, StatisticsDown.Min(f => f.Value) + "mbit/s");
					Set(LowNetwork, StatisticsDown.Max(f => f.Value) / 100 * 20 + "mbit/s");
				}
			}

			string ipperfClient = Environment.GetCommandLineArgs().FirstOrDefault(e => e.StartsWith("-ipperf="))?.Replace("-ipperf=", "");
			if (!string.IsNullOrWhiteSpace(ipperfClient))
			{
				SpeedProcessors.Add(new IPerfClient(_file, ipperfClient.Trim('\"')));
			}
			SpeedProcessors.Add(new SpeedTestDotNetClient());

			using (var fs = new FileStream(_file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 6124))
			{
				using (var sw = new StreamWriter(fs, Encoding.UTF8, (int) SteamBufferSize))
				{
					Timer = Task.Run(async () =>
					{
						while (!_stopRequested)
						{
							try
							{
								DoNow = false;
								NextInvoke = DateTime.Now.Add(CurrentWaitTime);
								Set(State, "Wait");
								while (NextInvoke > DateTime.Now && !DoNow)
								{
									await Task.Delay(TimeSpan.FromSeconds(1));
									PrintConsole();
									PingServer(sw, 1);
								}

								Set(State, "Measure");
								MeasureTime(sw);

								Set(State, "Ping");
								PingServer(sw, 5);
								fs.Flush();
							}
							catch (Exception e)
							{
							}
							finally
							{
								LastInvoke = DateTime.Now;
							}

							//await Task.Delay(_currentWaitTime);
						}
					});

					_cmdDispatcher = new ConsoleCommandDispatcher();
					_cmdDispatcher.ProvideLookup = false;
					_cmdDispatcher.ProvideHistory = false;

					_cmdDispatcher.Commands.Add(
						new DelegateCommand(ConsoleKey.H, txt => { DisplayHelpText = !DisplayHelpText; })
						{
							HelpText = "Help"
						});
					_cmdDispatcher.Commands.Add(new DelegateCommand(ConsoleKey.X, txt =>
					{
						sw.Flush();
						fs.Flush();
						_cmdDispatcher.StopDispatcherLoop = true;
					})
					{
						HelpText = "Flushes all Cached Data and stops the App"
					});
					_cmdDispatcher.Commands.Add(new DelegateCommand(ConsoleKey.R, txt => { RenderGrid(); })
					{
						HelpText = "Refreshes the Grid overview"
					});
					_cmdDispatcher.Commands.Add(new DelegateCommand(ConsoleKey.D, txt => { DoNow = true; })
					{
						HelpText = "Executes the Measurement at the next circle"
					});
					_cmdDispatcher.Commands.Add(new DelegateCommand(ConsoleKey.E, txt =>
					{
						var exportFile = Path.GetTempFileName();
						using (var eFs = new FileStream(exportFile, FileMode.Open, FileAccess.Write))
						{
							using (var eSw = new StreamWriter(eFs))
							{
								eSw.WriteLine("Download;Date");
								foreach (var statDown in StatisticsDown)
								{
									eSw.Write(statDown.Value.ToString("##.###"));
									eSw.Write(";");
									eSw.WriteLine(statDown.DateTime);
								}
							}
						}

						var exp = Path.ChangeExtension(exportFile, ".csv");
						File.Copy(exportFile, exp);
						File.Delete(exportFile);
						var process = Process.Start(exp);
						process.Exited += (sender, eventArgs) => { File.Delete(exp); };
					})
					{
						HelpText = "Exports all Performance data (Download and Date) to an csv file"
					});
					_cmdDispatcher.Run();
				}
			}
		}

		public IEnumerable<DataInfo> Yield()
		{
			return typeof(Program).GetProperties().Where(e => e.PropertyType == typeof(DataInfo))
				.Select(e => e.GetValue(this) as DataInfo)
				.Concat(PingDestinations.SelectMany(e => e.Yield()));
		}

		public void Set(DataInfo info, object value)
		{
			if (value == null && info.Value == null || value != null && value.Equals(info.Value))
			{
				return;
			}

			info.Value = value;
			RenderGrid();
		}

		private void RenderGrid()
		{
			lock (TextGrid)
			{
				Console.CursorLeft = 0;
				Console.CursorTop = 0;
				if (DisplayHelpText)
				{
					TextGrid.ExtraInfos.AppendLine("Help: ");
					foreach (var cmdDispatcherCommand in _cmdDispatcher.Commands)
					{
						TextGrid.ExtraInfos.AppendLine($"\t {cmdDispatcherCommand.Render()}");
					}
				}

				TextGrid.RenderGrid();
			}
		}

		public static void Main(string[] args)
		{
			new Program();
		}

		private void PingServer(StreamWriter streamWriter, int count)
		{
			var pingCall = 0;
			TextGrid.ExtraInfos.Clear();
			PingDestinations.AsParallel().ForAll(pingDestination =>
			{
				Interlocked.Add(ref pingCall, 1);
				var ping = new Ping();
				var pingSize = 32;

				//var infoText = "Ping to " + pingDestination.Destination + " with " + pingSize + " bytes of zeros: ";
				//TextGrid.ExtraInfos.AppendLine(infoText);
				//RenderGrid();
				//WriteLine(streamWriter, infoText);

				for (var i = 0; i < count; i++)
				{
					var infoText = "Ping to " + pingDestination.Destination + " with " + pingSize + " bytes of zeros: ";
					Set(State, "Ping " + pingCall + " of " + count * PingDestinations.Length);

					Set(pingDestination.PingsSend, pingDestination.Send++);
					PingReply pingReply;
					try
					{
						pingReply = ping.Send(pingDestination.Destination);
					}
					catch (Exception e)
					{
						infoText += "Error: " + e.Message;
						WriteLine(streamWriter, infoText);
						Set(pingDestination.PingsFailed, pingDestination.Fail++);
						continue;
					}
					finally
					{
						PrintConsole();
					}

					var dp = new DataPoint();
					dp.DateTime = DateTime.Now;
					dp.Value = pingReply?.RoundtripTime ?? 0;
					Pings.Add(dp);
					pingDestination.DataPoints.Add(dp);
					if (pingReply == null)
					{
						infoText += "Error: Unknown";
						WriteLine(streamWriter, infoText);
						Set(pingDestination.PingsFailed, pingDestination.Fail++);
						continue;
					}

					Set(pingDestination.PingAverage,
						Math.Round(pingDestination.DataPoints.Average(e => e.Value), 3) + "ms");
					var outp = "Ping result " + pingReply.Status + " time=" + pingReply.RoundtripTime + "ms TTL=" +
					           pingReply.Options?.Ttl ?? "?";

					infoText += outp;
					WriteLine(streamWriter, infoText);

					if (pingReply.Status != IPStatus.Success)
					{
						Set(pingDestination.PingsFailed, pingDestination.Fail++);
					}
				}

				Set(PingsCount, Pings.Count);
				PrintConsole();
			});
			TextGrid.ExtraInfos.Clear();

			Set(State, "Sleep");
			//Console.WriteLine("Average PingTime: " + ((5 - failed) != 0 ? avrPing / (5 - failed) : 0) + "ms");
		}

		public string WriteLine(StreamWriter stream, string line)
		{
			//Console.WriteLine(line);
			lock (stream)
			{
				line = DateTime.Now.ToString("R") + "|" + line;
				FileCacheSize += stream.Encoding.GetByteCount(line);
				if (FileCacheSize >= SteamBufferSize)
				{
					FileCacheSize = FileCacheSize - SteamBufferSize;
				}

				Set(FileCache, FileCacheSize);
				stream.WriteLine(line);
				return line;
			}
		}

		public void PrintConsole()
		{
			var percentLast10Minutes =
				Math.Round(
					(double) Pings.Where(e => e.DateTime > DateTime.Now.AddMinutes(-10)).Count(e => e.Value == 0) /
					Pings.Count * 100D);

			var last100 = Pings.OrderByDescending(e => e.DateTime).Take(100).ToArray();

			var percentLast1000 = Math.Round((double) last100.Count(e => e.Value == 0) / last100.Length * 100D);
			Set(TimeToNext, (NextInvoke - DateTime.Now).ToString("g"));
			if (StatisticsDown.Count > 4)
			{
				Set(LowNetwork, StatisticsDown.Max(f => f.Value) / 100 * 20 + "mbit/s");
			}
			Set(PacketLoss, percentLast10Minutes + "%");
			Set(PacketLossCurrently, percentLast1000 + "%");
			if (Pings.Count(e => e.Value != 0) > 0)
			{
				Set(PingAverage, Math.Round(Pings.Where(e => e.Value != 0).Average(f => f.Value)) + "ms");
			}

			Console.Title = $"Time: {TimeToNext.Value}; " +
			                $"LowNetwork: {LowNetwork.Value}; " +
			                $"PacketLoss: {PacketLoss.Value}; " +
			                $"Ping Avr: {PingAverage.Value}; ";
		}

		private void MeasureTime(StreamWriter state)
		{
			Output.Clear();
			TextGrid.ExtraInfos.Clear();
			Console.Clear();
			RenderGrid();

			var statsDown = new List<DataPoint>();
			var statsUp = new List<DataPoint>();
			foreach (var speedTestProcessor in SpeedProcessors)
			{
				var speedTestResult = speedTestProcessor.Measure(data =>
				{
					var line = WriteLine(state, data);
					Output.Add(line);
					TextGrid.ExtraInfos.AppendLine(data);
					RenderGrid();
				});
				state.Flush();
				Console.Clear();
				RenderGrid();
				foreach (var testResult in speedTestResult)
				{
					statsDown.Add(new DataPoint()
					{
						DateTime = DateTime.Now,
						Value = testResult.Receive
					});
					statsUp.Add(new DataPoint()
					{
						DateTime = DateTime.Now,
						Value = testResult.Send
					});
				}
			}

			StatisticsDown.Add(new DataPoint(DateTime.Now, statsDown.Select(e => e.Value).Average()));
			StatisticsUp.Add(new DataPoint(DateTime.Now, statsUp.Select(e => e.Value).Average()));
			

			if (StatisticsDown.Any())
			{
				Set(MinNetwork, StatisticsDown.Min(f => f.Value) + "mbit/s");
				Set(MaxNetwork, StatisticsDown.Max(f => f.Value) + "mbit/s");
				Set(AverageNetwork, Math.Round(StatisticsDown.Average(e => e.Value)) + "mbit/s");
			}

			Set(LastNetworkRecive,
				StatisticsDown.OrderByDescending(e => e.DateTime).FirstOrDefault()?.Value + "mbit/s");
			Set(LastNetworkSend,
				StatisticsUp.OrderByDescending(e => e.DateTime).FirstOrDefault()?.Value + "mbit/s");

			CheckForReducedTimer(state, StatisticsDown.Last().Value);
			Set(MeasurementsCount, StatisticsDown.Count / 2);
		}

		public void ImportStatistics(string[] lines)
		{
			foreach (var line in lines)
			{
				var lineParts = line.Split('|');

				if (lineParts.Length > 1)
				{
					var contentPart = lineParts[1];

					if (contentPart.StartsWith("Ping to"))
					{
						var pingOk = _pingOkRegex.Matches(line);
						var foundName = PingDestinations.FirstOrDefault(e => line.Contains(e.Destination));

						if (pingOk.Count > 3)
						{
							Console.WriteLine();
						}

						foreach (Match pingGroup in pingOk)
						{
							var pingPoint = new DataPoint();
							var speed = pingGroup.Groups[2];
							var status = pingGroup.Groups[1];
							var speedValue = decimal.Parse(speed.Value, NumberStyles.Number);

							pingPoint.DateTime = DateTime.Parse(lineParts[0]);
							pingPoint.Value = speedValue;

							if (foundName != null)
							{
								foundName.DataPoints.Add(pingPoint);
								foundName.Send++;
								if (!status.Value.Contains("Success"))
								{
									foundName.Fail++;
								}
							}

							Pings.Add(pingPoint);
						}

						foreach (Match match in _pingFailRegex.Matches(contentPart))
						{
							var pingPoint = new DataPoint();
							pingPoint.DateTime = DateTime.Parse(lineParts[0]);
							pingPoint.Value = 0;
							Pings.Add(pingPoint);
						}

						continue;
					}
				}

				foreach (var speedProcessor in SpeedProcessors)
				{
					foreach (var dataPoint in speedProcessor.ParseData(line))
					{
						StatisticsDown.Add(dataPoint);
					}
				}
			}
		}

		private void CheckForReducedTimer(StreamWriter writer, decimal latest)
		{
			if (StatisticsDown.Count < 4)
			{
				return;
			}

			//if (StatisticsDown.Count > 200)
			//{
			//	StatisticsDown.RemoveRange(200, StatisticsDown.Count - 200);
			//}

			var max = StatisticsDown.Max(f => f.Value);

			_lowerNetworkBound = max / 100 * 20;
			if (latest < _lowerNetworkBound && !FasterLookup)
			{
				CurrentWaitTime = FromMinutesSlowNetwork;
				FasterLookup = true;
				WriteLine(writer, "SLOW NETWORK DETECTED! Reducing wait time to " + FromMinutesSlowNetwork);
			}

			if (latest > _lowerNetworkBound && FasterLookup)
			{
				CurrentWaitTime = FromMinutes;
				FasterLookup = false;
				WriteLine(writer, "FAST NETWORK DETECTED! Expanding wait time to " + FromMinutes);
			}
		}

		private void ResetTimer()
		{
			if (FasterLookup)
			{
				CurrentWaitTime = FromMinutesSlowNetwork;
			}
			else
			{
				CurrentWaitTime = FromMinutes;
			}
		}

		public IList<ISpeedTestProcessor> SpeedProcessors { get; set; }

		public Task Timer { get; set; }
		public TimeSpan CurrentWaitTime { get; private set; }
		public TimeSpan FromMinutes { get; }
		public TimeSpan FromMinutesSlowNetwork { get; }
		public bool FasterLookup { get; set; }
		public DateTime NextInvoke { get; private set; }
		public DateTime LastInvoke { get; set; }

		public bool DoNow { get; set; }

		//public  long Send { get; set; }
		//public  long Failed { get; set; }
		//public  long AveragePing { get; set; }

		public ConcurrentBag<DataPoint> Pings { get; set; }
		public ConcurrentBag<DataPoint> StatisticsDown { get; set; }
		public ConcurrentBag<DataPoint> StatisticsUp { get; set; }

		public TextGrid<DataInfo> TextGrid { get; set; }

		public long FileCacheSize { get; set; }

		public PingInfo[] PingDestinations { get; set; }

		public List<string> Output { get; }

		public long SteamBufferSize { get; set; }

		public bool DisplayHelpText { get; private set; }

		#region States

		public DataInfo State { get; set; }
		public DataInfo TimeToNext { get; set; }
		public DataInfo LastNetworkSend { get; set; }
		public DataInfo LastNetworkRecive { get; set; }
		public DataInfo AverageNetwork { get; set; }

		public DataInfo LowNetwork { get; set; }
		public DataInfo MaxNetwork { get; set; }
		public DataInfo MinNetwork { get; set; }
		public DataInfo MeasurementsCount { get; set; }

		public DataInfo PingAverage { get; set; }
		public DataInfo PingsCount { get; set; }
		public DataInfo PacketLoss { get; set; }

		public DataInfo PacketLossCurrently { get; set; }

		public DataInfo Imported { get; set; }
		public DataInfo FileCache { get; set; }

		#endregion
	}
}