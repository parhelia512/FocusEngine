// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xenko.Core;
using Xenko.Core.Diagnostics;
using Xenko.Media;

namespace Xenko.Audio
{
    public abstract class DynamicSoundSource : IDisposable
    {
        public static Logger Logger = GlobalLogger.GetLogger(nameof(DynamicSoundSource));

        private static Thread readFromDiskWorker;

        protected static readonly ConcurrentQueue<DynamicSoundSource> NewSources = new ConcurrentQueue<DynamicSoundSource>();
        protected static readonly List<DynamicSoundSource> Sources = new List<DynamicSoundSource>();

        /// <summary>
        /// The possible async commands that can be queued and be handled by subclasses
        /// </summary>
        protected enum AsyncCommand
        {
            Play,
            Pause,
            Stop,
            Seek,
            SetRange,
            Dispose,
        }

        /// <summary>
        /// The commands derived classes should execute.
        /// </summary>
        protected readonly ConcurrentQueue<AsyncCommand> Commands = new ConcurrentQueue<AsyncCommand>(), StopCommands = new ConcurrentQueue<AsyncCommand>();

        private bool readyToPlay;
        private int prebufferedCount;
        private readonly int prebufferedTarget;
        private int nativeBufferSizeBytes;

        /// <summary>
        /// Gets a task that will be fired once the source is ready to play.
        /// </summary>
        public TaskCompletionSource<bool> ReadyToPlay { get; private set; } = new TaskCompletionSource<bool>(false);

        /// <summary>
        /// Gets a task that will be fired once there will be no more queueud data.
        /// </summary>
        public TaskCompletionSource<bool> Ended { get; private set; } = new TaskCompletionSource<bool>(false);

        protected readonly List<AudioLayer.Buffer> deviceBuffers = new List<AudioLayer.Buffer>();
        protected readonly Queue<AudioLayer.Buffer> freeBuffers = new Queue<AudioLayer.Buffer>(4);

        /// <summary>
        /// The sound instance associated.
        /// </summary>
        protected SoundInstance soundInstance;

        protected bool isInitialized;

        protected bool isDisposed;

        /// <summary>
        /// If we are in the paused state.
        /// </summary>
        protected PlayState state = PlayState.Stopped;

        /// <summary>
        /// If we are waiting to play.
        /// </summary>
        protected volatile bool playingQueued;

        /// <summary>
        /// Initializes a new instance of the <see cref="DynamicSoundSource"/> class.
        /// Sub classes can implement their own streaming sources.
        /// </summary>
        /// <param name="soundInstance">the sound instance associated.</param>
        /// <param name="numberOfBuffers">the size of the streaming ring-buffer.</param>
        /// <param name="maxBufferSizeBytes">the maximum size of each buffer.</param>
        protected DynamicSoundSource(SoundInstance soundInstance, int numberOfBuffers, int maxBufferSizeBytes)
        {
            nativeBufferSizeBytes = maxBufferSizeBytes;
            prebufferedTarget = (int)Math.Ceiling(numberOfBuffers / 3.0);

            this.soundInstance = soundInstance;
            for (var i = 0; i < numberOfBuffers; i++)
            {
                var buffer = AudioLayer.BufferCreate(nativeBufferSizeBytes);
                deviceBuffers.Add(buffer);
                freeBuffers.Enqueue(deviceBuffers[i]);
            }

            if (readFromDiskWorker == null)
            {
                readFromDiskWorker = new Thread(new ThreadStart(Worker));
                readFromDiskWorker.Priority = ThreadPriority.BelowNormal;
                readFromDiskWorker.IsBackground = true;
                readFromDiskWorker.Start();
            }
        }

        /// <summary>
        /// Enqueues a dispose command, to dispose this instance.
        /// </summary>
        public virtual void Dispose()
        {
            StopCommands.Enqueue(AsyncCommand.Dispose);
        }

        /// <summary>
        /// Checks if a buffer can be filled, before calling FillBuffer this should be checked.
        /// </summary>
        protected virtual bool CanFill
        {
            get
            {
                if (freeBuffers.Count > 0)
                    return true;

                var freeBuffer = AudioLayer.SourceGetFreeBuffer(soundInstance.Source);
                if (freeBuffer.Ptr == IntPtr.Zero)
                    return false;

                freeBuffers.Enqueue(freeBuffer);
                return true;
            }
        }

        /// <summary>
        /// Max number of buffers that are going to be queued.
        /// </summary>
        public abstract int MaxNumberOfBuffers { get; }

        /// <summary>
        /// Enqueues a Play command, to Play this instance.
        /// </summary>
        public void Play()
        {
            playingQueued = true;
            Commands.Enqueue(AsyncCommand.Play);
        }

        /// <summary>
        /// Enqueues a Pause command, to Pause this instance.
        /// </summary>
        public void Pause()
        {
            StopCommands.Enqueue(AsyncCommand.Pause);
        }

        /// <summary>
        /// Enqueues a Stop command, to Stop this instance.
        /// </summary>
        public void Stop()
        {
            StopCommands.Enqueue(AsyncCommand.Stop);
        }

        /// <summary>
        /// Gets if this instance is in the playing state.
        /// </summary>
        public bool IsPausedOrPlaying => playingQueued || state != PlayState.Stopped;

        /// <summary>
        /// Gets or sets the region of time to play from the audio clip.
        /// </summary>
        public virtual PlayRange PlayRange
        {
            get => new PlayRange(TimeSpan.Zero, TimeSpan.Zero);
            set => Commands.Enqueue(AsyncCommand.SetRange);
        }

        /// <summary>
        /// Sets if the stream should be played in loop.
        /// </summary>
        /// <param name="looped">if looped or not</param>
        public abstract void SetLooped(bool looped);

        protected virtual void InitializeInternal()
        {
            isInitialized = true;
            RestartInternal();
        }

        private void PlayAsyncInternal()
        {
            Task.Run(async () =>
            {
                var playMe = await ReadyToPlay.Task;
                if (playMe)
                    AudioLayer.SourcePlay(soundInstance.Source);
            });
        }

        /// <summary>
        /// Update the sound source
        /// </summary>
        protected virtual void UpdateInternal() { }

        /// <summary>
        /// Restarts streaming from the beginning.
        /// </summary>
        protected virtual void RestartInternal()
        {
            ReadyToPlay.TrySetResult(false);
            ReadyToPlay = new TaskCompletionSource<bool>();
            readyToPlay = false;
            prebufferedCount = 0;

            PrepareInternal();
        }

        /// <summary>
        /// Prepare the source for playback
        /// </summary>
        protected virtual void PrepareInternal() { }

        protected virtual void PlayInternal()
        {
            switch (state)
            {
                case PlayState.Playing:
                    break;
                case PlayState.Paused:
                    AudioLayer.SourcePlay(soundInstance.Source);
                    break;
                case PlayState.Stopped:
                    Ended.TrySetResult(false);
                    Ended = new TaskCompletionSource<bool>();
                    PlayAsyncInternal();
                    break;
            }
            playingQueued = false;
            state = PlayState.Playing;
        }

        protected virtual void PauseInternal()
        {
            state = PlayState.Paused;
            AudioLayer.SourcePause(soundInstance.Source);
        }

        protected virtual void StopInternal(bool ignoreQueuedBuffer = true)
        {
            Ended.TrySetResult(true);
            state = PlayState.Stopped;
            soundInstance.playState = PlayState.Stopped;
            if (ignoreQueuedBuffer) AudioLayer.SourceStop(soundInstance.Source);
            RestartInternal();
        }

        protected virtual void SeekInternal() { }

        /// <summary>
        /// Destroys the instance.
        /// </summary>
        protected virtual void DisposeInternal()
        {
            AudioLayer.SourceDestroy(soundInstance.Source);

            foreach (var deviceBuffer in deviceBuffers)
                AudioLayer.BufferDestroy(deviceBuffer);

            deviceBuffers.Clear();
            freeBuffers.Clear();
            isDisposed = true;
            isInitialized = false;
        }

        /// <summary>
        /// If CanFillis true with this method you can fill the next free buffer
        /// </summary>
        /// <param name="pcm">The pointer to PCM data</param>
        /// <param name="bufferSize">The full size in bytes of PCM data</param>
        /// <param name="type">If this buffer is the last buffer of the stream set to true, if not false</param>
        protected void FillBuffer(IntPtr pcm, int bufferSize, AudioLayer.BufferType type)
        {
            if (bufferSize > nativeBufferSizeBytes)
            {
                Logger.Error("Provided buffer size is bigger than native buffer. Data will be cut.");
                bufferSize = nativeBufferSizeBytes;
            }

            var buffer = freeBuffers.Dequeue();
            AudioLayer.SourceQueueBuffer(soundInstance.Source, buffer, pcm, bufferSize, type);
            if (readyToPlay) return;

            prebufferedCount++;
            if (prebufferedCount < prebufferedTarget) return;
            readyToPlay = true;
            ReadyToPlay.TrySetResult(true);
        }

        /// <summary>
        /// If CanFillis true with this method you can fill the next free buffer
        /// </summary>
        /// <param name="pcm">The array containing PCM data</param>
        /// <param name="bufferSize">The full size in bytes of PCM data</param>
        /// <param name="type">If this buffer is the last buffer of the stream set to true, if not false</param>
        protected unsafe void FillBuffer(short[] pcm, int bufferSize, AudioLayer.BufferType type)
        {
            fixed (void* pcmBuffer = pcm)
            {
                FillBuffer(new IntPtr(pcmBuffer), bufferSize, type);
            }
        }
        /// <summary>
        /// If CanFillis true with this method you can fill the next free buffer
        /// </summary>
        /// <param name="pcm">The array containing PCM data</param>
        /// <param name="bufferSize">The full size in bytes of PCM data</param>
        /// <param name="type">If this buffer is the last buffer of the stream set to true, if not false</param>
        protected unsafe void FillBuffer(byte[] pcm, int bufferSize, AudioLayer.BufferType type)
        {
            fixed (void* pcmBuffer = pcm)
            {
                FillBuffer(new IntPtr(pcmBuffer), bufferSize, type);
            }
        }

        protected abstract void ExtractAndFillData();

        private static unsafe void Worker()
        {
            while (true)
            {
                while (!NewSources.IsEmpty)
                {
                    if (!NewSources.TryDequeue(out var source))
                        continue;

                    if (!source.isInitialized)
                        source.InitializeInternal();

                    if (source.isInitialized)
                        Sources.Add(source);
                }

                // process stops, cleanups & pauses first
                for (int i = 0; i < Sources.Count; i++)
                {
                    var source = Sources[i];

                    if (source.isDisposed)
                    {
                        Sources.RemoveAt(i);
                        i--;
                        continue;
                    }

                    while (!source.StopCommands.IsEmpty)
                    {
                        AsyncCommand command;
                        if (!source.StopCommands.TryDequeue(out command)) continue;
                        switch (command)
                        {
                            case AsyncCommand.Pause:
                                source.PauseInternal();
                                break;
                            case AsyncCommand.Stop:
                                source.StopInternal();
                                break;
                            case AsyncCommand.Dispose:
                                source.DisposeInternal();
                                Sources.RemoveAt(i);
                                i--;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }

                // handle the rest of commands and playing
                for (int i=0; i<Sources.Count; i++)
                {
                    var source = Sources[i];

                    source.UpdateInternal();

                    var seekRequested = false;

                    while (!source.Commands.IsEmpty)
                    {
                        AsyncCommand command;
                        if (!source.Commands.TryDequeue(out command)) continue;
                        switch (command)
                        {
                            case AsyncCommand.Play:
                                source.PlayInternal();
                                break;
                            case AsyncCommand.Seek:
                                seekRequested = true;
                                break;
                            case AsyncCommand.SetRange:
                                source.RestartInternal();
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }

                    //Did we get a Seek request?
                    if (seekRequested)
                    {
                        source.SeekInternal();
                        continue;
                    }

                    // handle certain playing states
                    if (source.state == PlayState.Playing)
                    {
                        if (source.Ended.Task.IsCompleted)
                        {
                            // sound ended, stop it
                            source.StopInternal();
                        }
                        else if (source.CanFill)
                        {
                            source.ExtractAndFillData();
                        }
                        else if (AudioLayer.SourceIsPlaying(source.soundInstance.Source) == false)
                        {
                            // get music playing again
                            AudioLayer.SourceStop(source.soundInstance.Source);
                            source.RestartInternal();
                            if (source.CanFill) source.ExtractAndFillData();
                            AudioLayer.SourcePlay(source.soundInstance.Source);
                        }
                    }
                }

                // sleep after going through the music
                Thread.Sleep(100);
            }
        }
    }
}
