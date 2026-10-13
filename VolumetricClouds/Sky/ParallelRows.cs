using System;
using System.Threading;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Splits rows of a map over the processor's cores (1.5.0, for Larger weather pattern's maps,
    /// 16x the work of the usual ones). The calling thread takes the first share and waits for the
    /// others; a share that throws fails the whole run. Each row must be independent of the others.
    /// Pure: no engine calls.
    /// </summary>
    public static class ParallelRows
    {
        /// <summary>At most this many threads, the caller's included.</summary>
        public const int MaxThreads = 8;

        /// <summary>
        /// Runs <paramref name="work"/>(firstRow, endRow) over 0..<paramref name="rows"/> and returns how
        /// many threads did it.
        /// </summary>
        public static int Run(int rows, string name, Action<int, int> work)
        {
            int threads = Math.Max(1, Math.Min(Math.Min(Environment.ProcessorCount, MaxThreads), rows));
            var workers = new Thread[threads - 1];
            Exception failure = null;

            for (int t = 1; t < threads; t++)
            {
                int first = rows * t / threads, end = rows * (t + 1) / threads;
                workers[t - 1] = new Thread(() =>
                {
                    try
                    {
                        work(first, end);
                    }
                    catch (Exception e)
                    {
                        failure = e;
                    }
                })
                {
                    IsBackground = true,
                    Name = name,
                };
                workers[t - 1].Start();
            }

            work(0, rows / threads);

            for (int t = 0; t < workers.Length; t++)
                workers[t].Join();

            if (failure != null)
                throw new InvalidOperationException(name + " failed on a worker thread.", failure);

            return threads;
        }
    }
}
