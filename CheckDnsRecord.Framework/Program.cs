using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;

namespace CheckDnsRecord
{
	public class Program
	{
		public static void Main(string[] args)
		{
			Console.ForegroundColor = ConsoleColor.White;
			Console.WriteLine("Server: ");
			var input = Console.ReadLine();
			var server = string.IsNullOrWhiteSpace(input) ? "jean-pierre-bachmann.de" : input;
			Console.Title = server;

			Console.WriteLine("Find TXT Record:");
			input = Console.ReadLine();

			input = string.IsNullOrWhiteSpace(input) ? "SpEsZQ0YgIvvSFx3Vcd6Ssli7fYW0jtmTMcL829da3o" : input;

			var dnsServer = new List<DomainLookup>
			{
				new DomainLookup(IPAddress.Parse("8.8.8.8")),
				new DomainLookup(IPAddress.Parse("208.67.222.222")),
				new DomainLookup(IPAddress.Parse("208.67.222.123")),
				new DomainLookup(IPAddress.Parse("209.244.0.3")),
				new DomainLookup(IPAddress.Parse("216.146.35.35")),
				new DomainLookup(IPAddress.Parse("195.46.39.39")),
				new DomainLookup(IPAddress.Parse("8.26.56.26")),
				new DomainLookup(IPAddress.Parse("84.200.69.80")),
				new DomainLookup(IPAddress.Parse("81.169.163.106")),
				new DomainLookup(IPAddress.Parse("81.169.148.38")),
			};

			var dnsDomains = new[]
			{
				"ns1-06.azure-dns.com",
				"ns2-06.azure-dns.net",
				"ns3-06.azure-dns.org",
				"ns4-06.azure-dns.info",
				"ns1-08.azure-dns.com",
				"ns2-08.azure-dns.net",
				"ns3-08.azure-dns.org",
				"ns4-08.azure-dns.info",

				"a.root-servers.net",
			};

			foreach (var dnsDomain in dnsDomains)
			{
				foreach (var hostAddress in Dns.GetHostAddresses(dnsDomain).Where(e => e.AddressFamily == AddressFamily.InterNetwork))
				{
					dnsServer.Add(new DomainLookup(hostAddress));
				}
			}

			Console.Clear();
			Console.WriteLine("Check Dns:");
			var client = new LookupClient();
			client.UseCache = true;
			
			while (true)
			{
				foreach (var lookupClient in dnsServer)
				{
					if (lookupClient.ReverseLookup == null)
					{
						var dnsQueryResponse = client.GetHostName(lookupClient.Address);
						lookupClient.ReverseLookup = dnsQueryResponse;
					}
					Console.Write($"Check: {lookupClient.Address} - {lookupClient.ReverseLookup}");
					if (lookupClient.ErrorCounter > 10)
					{
						Console.WriteLine("Skipped");
					}

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

					IDnsQueryResponse txtQuery = null;
					IDnsQueryResponse nsQuery = null;
					string errorText = null;
					try
					{
						txtQuery = client.QueryServer(new[] {lookupClient.Address}, server, QueryType.TXT);
						errorText = txtQuery.HasError ? txtQuery.ErrorMessage : null;
						
						nsQuery = client.QueryServer(new[] {lookupClient.Address}, server, QueryType.NS);
						errorText = txtQuery.HasError ? txtQuery.ErrorMessage : null;
					}
					catch (Exception e)
					{
						errorText = e.Message;
					}
					finally
					{
						stopWaiter = true;
						stoppedWaiter.Wait();
					}
					Console.Write("   ");
					Console.CursorLeft -= "   ".Length;
					if (errorText != null)
					{
						lookupClient.ErrorCounter++;
						Console.WriteLine("...Error: " + errorText);
						continue;
					}

					Console.WriteLine();

					var txtRecords = txtQuery.Answers.TxtRecords().ToArray();
					var firstOrDefault = txtRecords.FirstOrDefault(e =>
						e.Text.Any(f => f.Equals(input)));
					var nsRecords = nsQuery.Answers.NsRecords();
					foreach (var nsRecord in nsRecords)
					{
						Console.ForegroundColor = ConsoleColor.Magenta;
						Console.WriteLine(nsRecord );
					}
					if (firstOrDefault != null)
					{
						Console.ForegroundColor = ConsoleColor.Green;
						Console.WriteLine("FOUND!");
					}
					else
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine("not found: ");
						foreach (var txtRecord in txtRecords)
						{
							Console.WriteLine(txtRecord);
						}
					}
					Console.ForegroundColor = ConsoleColor.White;
				}

				var nextUpdate = DateTime.Now.AddSeconds(150);
				var waitMessage = "Wait for next call: ";
				Console.Write(waitMessage);
				while (nextUpdate > DateTime.Now)
				{
					Console.CursorLeft = waitMessage.Length;
					Console.Write("                 ");
					Console.CursorLeft = waitMessage.Length;

					var at = nextUpdate - DateTime.Now;
					Console.Write($"{at.Hours}H {at.Minutes}M {at.Seconds}S");

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
		}

		public IPAddress Address { get; set; }
		public int ErrorCounter { get; set; }

		public string ReverseLookup { get; set; }
	}
}
