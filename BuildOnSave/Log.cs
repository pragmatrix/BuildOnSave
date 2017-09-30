using System;
using Serilog;
using Serilog.Events;

namespace BuildOnSave
{
	static class Log
	{
		static readonly ILogger log = new LoggerConfiguration()
			.WriteTo.Seq("http://localhost:5341")
			.MinimumLevel.Debug()
			.CreateLogger();

		public static void F(string template, params object[] values)
		{
			log.Write(LogEventLevel.Fatal, template, values);
		}

		public static void E(string template, params object[] values)
		{
			log.Write(LogEventLevel.Error, template, values);
		}

		public static void W(string template, params object[] values)
		{
			log.Write(LogEventLevel.Warning, template, values);
		}

		public static void I(string template, params object[] values)
		{
			log.Write(LogEventLevel.Information, template, values);
		}

		public static void D(string template, params object[] values)
		{
			log.Write(LogEventLevel.Debug, template, values);
		}
		public static void V(string template, params object[] values)
		{
			log.Write(LogEventLevel.Verbose, template, values);
		}

		public static void F(Exception e, string template, params object[] values)
		{
			log.Write(LogEventLevel.Fatal, e, template, values);
		}

		public static void E(Exception e, string template, params object[] values)
		{
			log.Write(LogEventLevel.Error, e, template, values);
		}

		public static void W(Exception e, string template, params object[] values)
		{
			log.Write(LogEventLevel.Warning, e, template, values);
		}

		public static void I(Exception e, string template, params object[] values)
		{
			log.Write(LogEventLevel.Information, e, template, values);
		}

		public static void D(Exception e, string template, params object[] values)
		{
			log.Write(LogEventLevel.Debug, e, template, values);
		}
		public static void V(Exception e, string template, params object[] values)
		{
			log.Write(LogEventLevel.Verbose, e, template, values);
		}
	}
}
