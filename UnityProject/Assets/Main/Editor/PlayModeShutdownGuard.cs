using System;
using System.Threading;
using ProjectF.Infrastructure;
using UnityEditor;
using UnityEngine;

namespace ProjectF.Editor
{
    /// <summary>
    /// Stage 11.5 — Editor-only safety net for play-mode shutdown.
    ///
    /// The runtime code now shuts down in the spec's order (cancel CTS →
    /// AWAIT every tracked background task with a bounded timeout → only then
    /// dispose store/logger/gRPC channel), wired through VContainer teardown.
    /// This guard is the seatbelt, not the fix:
    ///
    /// - On ExitingPlayMode: force-disposes the app-scope services (their
    ///   Dispose bridges run the ordered cancel → await → dispose sequence),
    ///   waits a bounded window, then reports any task from
    ///   <see cref="BackgroundTaskRegistry"/> that is STILL alive with its
    ///   name and running time — loudly, instead of hanging the domain
    ///   reload silently.
    /// - On beforeAssemblyReload (domain reload at play-mode exit, script
    ///   recompile, or editor quit): the same report, so the Console tells us
    ///   exactly which task did not stop even when OnDestroy ordering was
    ///   skipped entirely.
    ///
    /// Services reach the guard through the runtime-side
    /// <see cref="EditorShutdownHook"/> static (RootLifetimeScope fills it at
    /// container build; the runtime asmdef cannot reference this editor
    /// asmdef, so the bridge flows editor←runtime only).
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeShutdownGuard
    {
        /// <summary>How long the guard waits for the runtime teardown before
        /// reporting survivors (the 3s per-task await + margin).</summary>
        private const int GraceMs = 4000;

        static PlayModeShutdownGuard()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingPlayMode:
                    ForceCancelAndReport("ExitingPlayMode");
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    // Last-chance report: teardown should be long done; if
                    // something is still alive, name it now.
                    ReportSurvivors("EnteredEditMode");
                    EditorShutdownHook.Clear();
                    break;
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            // Domain reload can happen at play-mode exit, on script
            // recompile during play, or at editor quit. Everything tracked
            // must be stopped BEFORE the reload or the reload hangs.
            ForceCancelAndReport("beforeAssemblyReload");
        }

        private static void ForceCancelAndReport(string trigger)
        {
            double started = EditorApplication.timeSinceStartup;

            // Stage 16: thread census around teardown — the reload hang
            // survived a clean tracked-task report, so measure what the
            // registry cannot see (NetMQ/transport threads).
            int threadsBefore = CountThreads();
            if (threadsBefore > 0)
            {
                Debug.Log($"[shutdown-guard] {trigger}: teardown begin — threads: {threadsBefore}");
            }

            // 1. Force the ordered shutdown now (idempotent — each service's
            //    Dispose re-runs cancel → await → dispose safely, and
            //    VContainer teardown would do the same moments later).
            try
            {
                (EditorShutdownHook.Presence as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[shutdown-guard] presence dispose failed: {ex.Message}");
            }

            try
            {
                // NOTE: a null hook means NO play session ran (e.g. a script
                // recompile outside play mode) — nothing to tear down, stay
                // silent. Only a NON-null non-IDisposable is a real defect:
                // the concrete class must declare IDisposable or VContainer
                // AND this guard both skip it (the original swarm leak).
                if (EditorShutdownHook.Chain is null)
                {
                    // Nothing tracked — no play session in this domain.
                }
                else if (EditorShutdownHook.Chain is not IDisposable chainDisposable)
                {
                    Debug.LogError(
                        "[shutdown-guard] LibplanetClient does NOT implement IDisposable — " +
                        "teardown cannot stop the swarm. Re-introduce the declaration!");
                }
                else
                {
                    chainDisposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[shutdown-guard] chain dispose failed: {ex.Message}");
            }

            try
            {
                EditorShutdownHook.StateWatcher?.Dispose();
                EditorShutdownHook.Monitor?.Dispose();
                EditorShutdownHook.ActionQueue?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[shutdown-guard] watcher/monitor/queue dispose failed: {ex.Message}");
            }

            // 2. Bounded window: let the async teardown finish on the
            //    threadpool; exit early the moment nothing is alive.
            while (EditorApplication.timeSinceStartup - started < GraceMs / 1000.0)
            {
                BackgroundTaskRegistry? registry = EditorShutdownHook.Registry;
                if (registry is null || registry.AliveTasks().Length == 0)
                {
                    break; // clean — don't burn the full window
                }

                Thread.Sleep(50);
            }

            // 3. Loudly report anything that refused to die.
            ReportSurvivors(trigger);

            int threadsAfter = CountThreads();
            if (threadsBefore > 0 && threadsAfter > 0)
            {
                Debug.Log($"[shutdown-guard] {trigger}: teardown end — threads: " +
                          $"{threadsBefore}→{threadsAfter}");
            }
        }

        /// <summary>Process-wide OS thread count — -1 when unavailable.
        /// Process.Threads is 0 on Unity Mono, so walk the Win32 toolhelp
        /// snapshot: NetMQ pollers / gRPC tokio workers never appear in
        /// BackgroundTaskRegistry; this is how we SEE them.</summary>
        private static int CountThreads()
        {
            try
            {
                const uint TH32CS_SNAPTHREAD = 0x4;
                IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
                if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
                {
                    return -1;
                }

                try
                {
                    uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                    var entry = new THREADENTRY32
                    {
                        dwSize = (uint)System.Runtime.InteropServices.Marshal
                            .SizeOf(typeof(THREADENTRY32)),
                    };
                    int count = 0;
                    if (Thread32First(snapshot, ref entry))
                    {
                        do
                        {
                            if (entry.th32OwnerProcessID == pid)
                            {
                                count++;
                            }
                        }
                        while (Thread32Next(snapshot, ref entry));
                    }

                    return count;
                }
                finally
                {
                    CloseHandle(snapshot);
                }
            }
            catch
            {
                return -1;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool Thread32First(IntPtr snapshot, ref THREADENTRY32 entry);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool Thread32Next(IntPtr snapshot, ref THREADENTRY32 entry);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct THREADENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ThreadID;
            public uint th32OwnerProcessID;
            public int tpBasePri;
            public int tpDeltaPri;
            public uint dwFlags;
        }

        private static void ReportSurvivors(string trigger)
        {
            BackgroundTaskRegistry? registry = EditorShutdownHook.Registry;
            if (registry is null)
            {
                return;
            }

            (string Name, TimeSpan Age)[] alive = registry.AliveTasks();
            if (alive.Length == 0)
            {
                Debug.Log($"[shutdown-guard] {trigger}: all tracked background tasks stopped cleanly.");
                return;
            }

            foreach ((string name, TimeSpan age) in alive)
            {
                Debug.LogWarning(
                    "[shutdown-guard] BACKGROUND TASK STILL ALIVE after teardown trigger '" +
                    trigger + "': '" + name + "' — running for " + age +
                    ". This task would have hung the domain reload. Report it to Stage 11.5 follow-up.");
            }
        }
    }
}
