using System;

namespace AppendDateTime
{
	public class DataPoint
	{
		public DataPoint()
		{
			
		}

		public DataPoint(DateTime dateTime, decimal value)
		{
			DateTime = dateTime;
			Value = value;
		}

		public DateTime DateTime { get; set; }
		public decimal Value { get; set; }
	}
}