using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CatiaWingDesigner.App.Catia
{
    internal sealed class CatiaComMessageFilter : IOleMessageFilter
    {
        private const int ServerCallIsHandled = 0;
        private const int ServerCallRetryLater = 2;
        private const int PendingMessageWaitDefaultProcess = 2;
        private const int RetryImmediately = 99;

        private static readonly object SyncRoot = new object();
        private static CatiaComMessageFilter? _currentFilter;
        private static IOleMessageFilter? _previousFilter;
        private static int _registrationDepth;

        public static void Register()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                throw new InvalidOperationException("CATIA COM 调用必须在 STA 线程执行。");
            }

            lock (SyncRoot)
            {
                if (_registrationDepth == 0)
                {
                    _currentFilter = new CatiaComMessageFilter();
                    var result = CoRegisterMessageFilter(_currentFilter, out _previousFilter);
                    if (result < 0)
                    {
                        Marshal.ThrowExceptionForHR(result);
                    }
                }

                _registrationDepth++;
            }
        }

        public static void Revoke()
        {
            lock (SyncRoot)
            {
                if (_registrationDepth == 0)
                {
                    return;
                }

                _registrationDepth--;
                if (_registrationDepth == 0)
                {
                    var result = CoRegisterMessageFilter(_previousFilter, out _);
                    _previousFilter = null;
                    _currentFilter = null;
                    if (result < 0)
                    {
                        Marshal.ThrowExceptionForHR(result);
                    }
                }
            }
        }

        public int HandleInComingCall(int dwCallType, IntPtr htaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        {
            return ServerCallIsHandled;
        }

        public int RetryRejectedCall(IntPtr htaskCallee, int dwTickCount, int dwRejectType)
        {
            return dwRejectType == ServerCallRetryLater ? RetryImmediately : -1;
        }

        public int MessagePending(IntPtr htaskCallee, int dwTickCount, int dwPendingType)
        {
            return PendingMessageWaitDefaultProcess;
        }

        [DllImport("ole32.dll")]
        private static extern int CoRegisterMessageFilter(
            IOleMessageFilter? newFilter,
            out IOleMessageFilter? oldFilter);
    }

    [ComImport]
    [Guid("00000016-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(
            int dwCallType,
            IntPtr htaskCaller,
            int dwTickCount,
            IntPtr lpInterfaceInfo);

        [PreserveSig]
        int RetryRejectedCall(
            IntPtr htaskCallee,
            int dwTickCount,
            int dwRejectType);

        [PreserveSig]
        int MessagePending(
            IntPtr htaskCallee,
            int dwTickCount,
            int dwPendingType);
    }
}
