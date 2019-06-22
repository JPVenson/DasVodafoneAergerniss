using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;

namespace CheckDnsRecord
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var server = "jean-pierre-bachmann.de";
			var dnsServer = new[]
			{
				new DomainLookup(IPAddress.Parse("8.8.8.8")),
				new DomainLookup(IPAddress.Parse("208.67.222.222")),
				new DomainLookup(IPAddress.Parse("208.67.222.123")),
				new DomainLookup(IPAddress.Parse("209.244.0.3")),
				new DomainLookup(IPAddress.Parse("156.154.70.1")),
				new DomainLookup(IPAddress.Parse("216.146.35.35")),
				new DomainLookup(IPAddress.Parse("195.46.39.39")),
				new DomainLookup(IPAddress.Parse("8.26.56.26")),
				new DomainLookup(IPAddress.Parse("84.200.69.80")),
				new DomainLookup(IPAddress.Parse("81.169.163.106")),
			};
			Console.WriteLine("Check Dns:");

			while (true)
			{
				foreach (var lookupClient in dnsServer)
				{
					Console.Write("Check: " + lookupClient.Address);
					var stopWaiter = false;
					var stoppedWaiter = new ManualResetEventSlim();
					Task.Run(async () =>
					{
						var waiterArray = new[] { '|', '/', '-', '\\', '-' };
						var index = 0;
						while (!stopWaiter)
						{
							if (index == waiterArray.Length)
							{
								index = 0;
							}
							Console.Write(waiterArray[index++]);
							await Task.Delay(140);
							Console.CursorLeft--;
						}
						stoppedWaiter.Set();
					});
					Console.Write("   ");

					var dnsQueryResponse = lookupClient.LookupClient.Query(server, QueryType.TXT);
					stopWaiter = true;
					stoppedWaiter.Wait();
					Console.CursorLeft--;
					if (dnsQueryResponse.HasError)
					{
						Console.WriteLine("...Error: " + dnsQueryResponse.ErrorMessage);
						continue;
					}

					var txtRecords = dnsQueryResponse.Answers.TxtRecords().ToArray();
					var firstOrDefault = txtRecords.FirstOrDefault(e =>
						e.Text.Any(f => f.Equals("zS0dfB3j13scLiPjBDDFvru49JYWJEMDVValEcG3Zjg")));
					if (firstOrDefault != null)
					{
						Console.ForegroundColor = ConsoleColor.Green;
						Console.WriteLine("\t\tFOUND!");
						Console.ForegroundColor = ConsoleColor.White;
					}
					else
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine("\t\tnot found: ");
						foreach (var txtRecord in txtRecords)
						{
							Console.WriteLine(txtRecord);
						}
						Console.ForegroundColor = ConsoleColor.White;
					}
				}

				int waitCounter = 30;
				var waitMessage = "Wait for next call: ";
				Console.Write(waitMessage);
				while (waitCounter > 0)
				{
					Console.CursorLeft = waitMessage.Length;
					Console.Write("                 ");
					Console.CursorLeft = waitMessage.Length;
					Console.Write(waitCounter--);

					Thread.Sleep(1000);
				}

				Console.Clear();
			}
		}
	}

	public class DomainLookup
	{
		public DomainLookup(IPAddress address)
		{
			Address = address;
			LookupClient = new LookupClient(Address);
		}

		public LookupClient LookupClient { get; private set; }
		public IPAddress Address { get; set; }
	}
}
