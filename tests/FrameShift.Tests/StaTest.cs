using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace FrameShift.Tests;

internal static class StaTest
{
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
