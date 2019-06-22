using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using SpeedTest.Models;

namespace AppendDateTime.SpeedTest
{
	public class SpeedTestDotNetClient : ISpeedTestProcessor
	{
		private SpeedTestClient _client;
		public SpeedTestDotNetClient()
		{
			SelectedServer = new List<Server>();
			_client = new SpeedTestClient();
			SelectServer();
		}
		Random random = new Random();

		public IList<Server> SelectedServer { get; set; }

		private IEnumerable<Server> SelectServersFrom(IList<Server> localServers, int count)
		{
			var server = new List<Server>();
			for (int i = 0; i < Math.Min(count, localServers.Count()); i++)
			{
				var sev = localServers[random.Next(0, localServers.Count)];
				if (server.Contains(sev))
				{
					i--;
					continue;
				}
				server.Add(sev);
			}

			return server;
		}

		private void SelectServer()
		{
			var localServers = _client.GetSettings().Servers.Where(e => e.Distance < 300000).ToList();
			var bestServer = localServers.OrderBy(x => x.Latency).First();
			var server = new List<Server>();
			server.Add(bestServer);

			//select near servers
			var partionSize = localServers.Count / 4;
			server.AddRange(SelectServersFrom(localServers.OrderBy(e => e.Distance)
				.Take(partionSize)
				.ToArray(), 2));

			//select middle near servers
			server.AddRange(SelectServersFrom(localServers.OrderBy(e => e.Distance)
				.Skip(partionSize)
				.Take(partionSize).ToArray(), 2));

			//select random servers
			server.AddRange(SelectServersFrom(localServers, 2));

			SelectedServer = server.ToList();
		}

		private void ReplaceServer(Server server)
		{
			var localServers = _client.GetSettings().Servers.Where(e => e.Distance < 600000).ToList();
			SelectedServer.Remove(server);
			SelectedServer.Add(localServers[random.Next(0, localServers.Count)]);
		}

		/// <inheritdoc />
		public IEnumerable<SpeedTestResult> Measure(Action<string> output)
		{
			foreach (var server in SelectedServer.ToArray())
			{
				var testDownloadSpeed = _client.TestDownloadSpeed(server, 2, 1) / 1000;
				if (testDownloadSpeed.Equals(Double.NaN))
				{
					output("Server seems unresponsive it will be replaced next circle");
					ReplaceServer(server);
					continue;
				}
				var host = new Uri(server.Url).Host;
				var ping = new Ping();

				var buffer = new byte[1] { 0x01 };
				var traceRoute = new List<PingReply>();
				for (int i = 0; i < 25; i++)
				{
					var result = ping.Send(host, 5000, buffer, new PingOptions(i + 1, true));
					traceRoute.Add(result);
					if (result?.Status == IPStatus.Success)
					{
						break;
					}
				}

				output($"Traced '{host}' to be '{traceRoute.Count}' hops and '{traceRoute.Select(e => e.RoundtripTime).Sum()}' ms roundtrip");

				output($"SpeedTest at '{DateTime.Now}' to '{host}' Download '{testDownloadSpeed}' mbit/s");
				var testUploadSpeed = _client.TestUploadSpeed(server) / 1000;
				output($"SpeedTest at '{DateTime.Now}' to '{host}' Upload '{testUploadSpeed}' mbit/s");
				yield return new SpeedTestResult()
				{
					Receive = (decimal)testDownloadSpeed,
					Send = (decimal)testUploadSpeed,
				};
			}
		}

		/// <inheritdoc />
		public IEnumerable<DataPoint> ParseData(string line)
		{
			var regxDownload = new Regex("SpeedTest at '(.*)' to '.*' Download '(.*)'");
			var regxUpload = new Regex("SpeedTest at '(.*)' to '.*' Download '(.*)'");
			var downMatch = regxDownload.Match(line);
			var upMatch = regxUpload.Match(line);
			if (downMatch.Success)
			{
				yield return new DataPoint(DateTime.Parse(downMatch.Groups[1].Value), Decimal.Parse(downMatch.Groups[2].Value));
			}
			if (upMatch.Success)
			{
				yield return new DataPoint(DateTime.Parse(upMatch.Groups[1].Value), Decimal.Parse(upMatch.Groups[2].Value));
			}
		}
	}
}