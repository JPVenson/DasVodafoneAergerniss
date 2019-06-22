using System;
using System.Collections.Generic;

namespace AppendDateTime
{
	public interface ISpeedTestProcessor
	{
		IEnumerable<SpeedTestResult> Measure(Action<string> output);
		IEnumerable<DataPoint> ParseData(string line);
	}
}