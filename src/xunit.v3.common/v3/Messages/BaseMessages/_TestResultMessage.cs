﻿namespace Xunit.v3
{
	/// <summary>
	/// This is the base message for all individual test results (e.g., tests which
	/// pass, fail, or are skipped).
	/// </summary>
	public class _TestResultMessage : _TestMessage, _IExecutionMetadata
	{
		/// <inheritdoc/>
		public decimal? ExecutionTime { get; set; }

		/// <inheritdoc/>
		public string? Output { get; set; }
	}
}
