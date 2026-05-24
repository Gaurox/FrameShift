namespace FrameShift.Core.AI.SeparateAudio;

public readonly record struct AudioSeparationProgress(int Percent, int ChunkIndex, int ChunkTotal, string Status);
