using System.Collections.Generic;

namespace AppendDateTime
{
	public class PingInfo
	{
		private int _send;

		public PingInfo(string destination)
		{
			Destination = destination;
			PingsSend = new DataInfo { Name = Destination + " " + nameof(PingsSend) };
			PingsFailed = new DataInfo { Name = Destination + " " + nameof(PingsFailed) };
			PingAverage = new DataInfo { Name = Destination + " " + nameof(PingAverage) };
			DataPoints = new List<DataPoint>();
		}

		public string Destination { get; set; }

		public int Send
		{
			get { return _send; }
			set { _send = value; }
		}

		public int Fail { get; set; }

		public List<DataPoint> DataPoints { get; set; }

		public DataInfo PingsSend { get; set; }
		public DataInfo PingsFailed { get; set; }
		public DataInfo PingAverage { get; set; }

		public IEnumerable<DataInfo> Yield()
		{
			yield return PingAverage;
			yield return PingsFailed;
			yield return PingsSend;
		}
	}
}