using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AppendDateTime.IPerf3
{
	public class IPerfClient : ISpeedTestProcessor
	{
		private readonly string _iperfLocation;
		private readonly string _server;

		public IPerfClient(string iperfLocation, string server)
		{
			_iperfLocation = iperfLocation;
			_server = server;
		}

		/// <param name="output"></param>
		/// <inheritdoc />
		public IEnumerable<SpeedTestResult> Measure(Action<string> output)
		{
			var process = new Process();
			var directoryName = Path.GetDirectoryName(_iperfLocation);
			process.StartInfo = new ProcessStartInfo(Path.Combine(directoryName, @"iperf3.exe"),
				$@"-4 -c {_server} -f m -b 1000M -N -R");

			process.EnableRaisingEvents = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.RedirectStandardOutput = true;
			var outputData = new List<string>();
			process.OutputDataReceived += (sender, args) =>
			{
				if (args.Data == null)
				{
					return;
				}

				output(args.Data);
				outputData.Add(args.Data);
			};
			process.Start();
			process.BeginOutputReadLine();
			process.WaitForExit();

			var stats = outputData.SelectMany(ParseData);

			yield return new SpeedTestResult()
			{
				Receive = (decimal) stats.OrderByDescending(e => e.DateTime).FirstOrDefault()?.Value,
				Send = (decimal) stats.OrderByDescending(e => e.DateTime).Skip(1).FirstOrDefault()?.Value,
			};
		}

		private readonly Regex _findSummeryRegEx = new Regex(@"(\d*\.\d*)[^\d]*\s*(sender|receiver)");
		private Regex _findPingSummeryRegEx = new Regex(@"Ping result (.*) time=(\d*)");

		/// <inheritdoc />
		public IEnumerable<DataPoint> ParseData(string line)
		{
			var groups = _findSummeryRegEx.Matches(line);
			foreach (Match group in groups)
			{
				var dp = new DataPoint();
				var speed = group.Groups[1];
				var speedValue = decimal.Parse(speed.Value.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator), NumberStyles.Number);
				dp.Value = speedValue;
				if (DateTime.TryParse(line.Split('|').First(), out var date))
				{
					dp.DateTime = date;
				}
				else
				{
					dp.DateTime = DateTime.Now;
				}
				yield return dp;
			}
		}
	}
}
