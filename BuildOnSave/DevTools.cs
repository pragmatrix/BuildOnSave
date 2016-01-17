using System;
using System.Diagnostics;

namespace BuildOnSave
{
	static class DevTools
	{
		public static IDisposable measureBlock(string context)
		{
#if !DEBUG
			return NullDisposeAction;
#else
			var sw = new Stopwatch();
			sw.Start();

			return new DisposeAction(() =>
			{
				var time = sw.Elapsed;
				Log.D(context + ": {@tm}", time);
			});
#endif
		}

		struct DisposeAction : IDisposable
		{
			readonly Action _action;

			public DisposeAction(Action action)
			{
				_action = action;
			}

			public void Dispose()
			{
				_action();
			}
		}

		static readonly IDisposable NullDisposeAction = new DisposeAction(() => { });
	}
}
