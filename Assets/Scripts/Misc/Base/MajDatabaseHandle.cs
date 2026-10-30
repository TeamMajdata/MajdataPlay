using SQLite;
using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace MajdataPlay
{
    /// <summary>
    /// Owns the single connection to one SQLite database file managed by <see cref="MajDatabase"/> and serializes every
    /// access to it.
    /// </summary>
    /// <remarks>
    /// Synchronous calls run on the calling thread under the connection's lock. Queued writes run on the thread pool one
    /// after another in call order. The connection itself is never exposed outside a callback, so only
    /// <see cref="MajDatabase"/> can close it, and closing always waits for the queued writes first.
    /// </remarks>
    internal sealed class MajDatabaseHandle
    {
        /// <summary>Gets the name of the database, used in log messages and exceptions.</summary>
        public string Name { get; }
        /// <summary>Gets whether the handle has started closing; queued writes are refused from then on.</summary>
        public bool IsClosed
        {
            get
            {
                lock (_queueLock)
                {
                    return _isClosing;
                }
            }
        }

        /// <summary>The open connection, accessed only under <see cref="_connectionLock"/>.</summary>
        private readonly SQLiteConnection _connection;
        /// <summary>Guards <see cref="_connection"/> and <see cref="_isDisposed"/>.</summary>
        private readonly object _connectionLock = new();
        /// <summary>Guards <see cref="_writeTail"/> and <see cref="_isClosing"/>.</summary>
        private readonly object _queueLock = new();
        /// <summary>Stores the completion of the last queued write; it never faults, so later writes always run.</summary>
        private Task _writeTail = Task.CompletedTask;
        /// <summary>Records whether closing has started.</summary>
        private bool _isClosing;
        /// <summary>Records whether the connection has been disposed.</summary>
        private bool _isDisposed;

        /// <summary>Wraps an open connection.</summary>
        /// <param name="name">The name of the database, used in log messages and exceptions.</param>
        /// <param name="connection">The open connection, which the handle owns from now on.</param>
        internal MajDatabaseHandle(string name, SQLiteConnection connection)
        {
            Name = name;
            _connection = connection;
        }

        /// <summary>Runs a read on the calling thread.</summary>
        /// <typeparam name="T">The type of the value read.</typeparam>
        /// <param name="read">The read, which must not keep the connection after it returns.</param>
        /// <returns>The value returned by <paramref name="read"/>.</returns>
        /// <exception cref="ObjectDisposedException">The handle has been closed.</exception>
        /// <exception cref="SQLiteException">The database rejected the read.</exception>
        public T Read<T>(Func<SQLiteConnection, T> read)
        {
            lock (_connectionLock)
            {
                ThrowIfDisposed();
                return read(_connection);
            }
        }
        /// <summary>Runs a write inside a transaction on the calling thread.</summary>
        /// <param name="write">The write, which must not keep the connection after it returns.</param>
        /// <remarks>
        /// The write runs immediately between two queued writes, so it is not ordered after writes queued by
        /// <see cref="WriteAsync"/>. A failure rolls the transaction back.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The handle has been closed.</exception>
        /// <exception cref="SQLiteException">The database rejected the write.</exception>
        public void Write(Action<SQLiteConnection> write)
        {
            lock (_connectionLock)
            {
                ThrowIfDisposed();
                _connection.RunInTransaction(() => write(_connection));
            }
        }
        /// <summary>Runs a read on the thread pool.</summary>
        /// <typeparam name="T">The type of the value read.</typeparam>
        /// <param name="read">The read, which must not keep the connection after it returns.</param>
        /// <returns>A task that completes with the value returned by <paramref name="read"/>.</returns>
        /// <exception cref="ObjectDisposedException">The handle has been closed; reported through the task.</exception>
        /// <exception cref="SQLiteException">The database rejected the read; reported through the task.</exception>
        public Task<T> ReadAsync<T>(Func<SQLiteConnection, T> read)
        {
            return Task.Run(() => Read(read));
        }
        /// <summary>Queues a write that runs inside a transaction after every write queued before it.</summary>
        /// <param name="write">The write, which must not keep the connection after it returns.</param>
        /// <returns>A task that completes when the write has been committed, or faults when it failed.</returns>
        /// <remarks>A failed write is rolled back and does not stop the writes queued after it.</remarks>
        /// <exception cref="ObjectDisposedException">The handle has started closing; reported through the task.</exception>
        /// <exception cref="SQLiteException">The database rejected the write; reported through the task.</exception>
        public Task WriteAsync(Action<SQLiteConnection> write)
        {
            lock (_queueLock)
            {
                if (_isClosing)
                {
                    return Task.FromException(new ObjectDisposedException(Name, $"The {Name} database is closed."));
                }
                var task = RunQueuedWriteAsync(_writeTail, write);
                _writeTail = task.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return task;
            }
        }
        /// <summary>Gets a task that completes when every write queued so far has finished.</summary>
        /// <returns>The completion of the last queued write; it never faults.</returns>
        public Task WaitForWritesAsync()
        {
            lock (_queueLock)
            {
                return _writeTail;
            }
        }
        /// <summary>Refuses new queued writes, waits for the queued ones and then closes the connection.</summary>
        /// <returns>A task that completes when the connection is closed.</returns>
        /// <remarks>
        /// Only <see cref="MajDatabase"/> calls this: closing while a transaction commits crashes SQLite natively, so the
        /// connection is closed only after the queue has drained. Calling it again waits for the same queue.
        /// </remarks>
        /// <exception cref="SQLiteException">Closing the connection failed.</exception>
        internal async Task CloseAsync()
        {
            Task pending;
            lock (_queueLock)
            {
                _isClosing = true;
                pending = _writeTail;
            }
            await pending.ConfigureAwait(false);
            lock (_connectionLock)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
                _connection.Dispose();
            }
        }
        /// <summary>Runs one queued write after the previous one.</summary>
        /// <param name="previous">The previously queued write, which never faults.</param>
        /// <param name="write">The write to run inside a transaction.</param>
        /// <returns>A task that completes when the write has been committed, or faults when it failed.</returns>
        private async Task RunQueuedWriteAsync(Task previous, Action<SQLiteConnection> write)
        {
            await previous.ConfigureAwait(false);
            await Task.Run(() => Write(write)).ConfigureAwait(false);
        }
        /// <summary>Throws when the connection has been disposed.</summary>
        /// <exception cref="ObjectDisposedException">The connection has been disposed.</exception>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(Name, $"The {Name} database is closed.");
            }
        }
    }
}
