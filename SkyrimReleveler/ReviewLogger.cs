using System;
using System.IO;
using Mutagen.Bethesda.Skyrim;

namespace SkyrimReleveler
{
    public record ReviewEntry(
        string EditorId,
        string FormKeyHex,
        string OriginPlugin,
        string TierName,
        int    PipelineLevel,
        int    FloorLevel,
        int    FinalLevel,
        float  ImportanceScore,
        float  Confidence,
        string FiredSignals);

    public sealed class ReviewLogger : IDisposable
    {
        private StreamWriter? _writer;
        private bool _disabled;
        private int _count;

        public ReviewLogger(string filePath)
        {
            try
            {
                _writer = new StreamWriter(filePath, append: false);
                _writer.WriteLine("EditorId,FormKey,OriginPlugin,Tier,PipelineLevel,FloorLevel,FinalLevel,ImportanceScore,Confidence,FiredSignals");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WARNING] ReviewLogger: could not open review_npcs.log: {ex.Message}");
                _disabled = true;
            }
        }

        public void Write(ReviewEntry e)
        {
            if (_disabled || _writer is null) return;
            _writer.WriteLine($"{e.EditorId},{e.FormKeyHex},{e.OriginPlugin},{e.TierName},{e.PipelineLevel},{e.FloorLevel},{e.FinalLevel},{e.ImportanceScore:F4},{e.Confidence:F4},{e.FiredSignals}");
            _count++;
        }

        public void MaybeWrite(INpcGetter npc, NpcAssessment assessment, int pipelineLevel,
            int floorLevel, int finalLevel, ScoreResult scoreResult, ImportanceWeights weights)
        {
            if (_disabled) return;

            bool lowConfidence = scoreResult.Confidence < weights.LowConfidenceThreshold;
            bool largeDelta    = pipelineLevel > 0 &&
                                 (finalLevel - pipelineLevel) / (float)pipelineLevel * 100f > weights.FloorDeltaThreshold;
            bool unpeeredMod   = assessment.IsModOrigin && !assessment.HasPeerGroup;

            if (!lowConfidence && !largeDelta && !unpeeredMod) return;

            Write(new ReviewEntry(
                EditorId:       npc.EditorID ?? "(null)",
                FormKeyHex:     npc.FormKey.ToString(),
                OriginPlugin:   npc.FormKey.ModKey.FileName,
                TierName:       TierSystem.GetName(assessment.Tier),
                PipelineLevel:  pipelineLevel,
                FloorLevel:     floorLevel,
                FinalLevel:     finalLevel,
                ImportanceScore: scoreResult.Score,
                Confidence:     scoreResult.Confidence,
                FiredSignals:   string.Join(",", scoreResult.FiredSignals)));
        }

        public void Dispose()
        {
            if (_disabled || _writer is null) return;
            if (_count == 0)
                _writer.WriteLine("# No NPCs required review.");
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
