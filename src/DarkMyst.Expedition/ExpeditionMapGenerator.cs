using System.Collections.Generic;
using DarkMyst.Combat;
using DarkMyst.Content;
using DarkMyst.Expedition.Model;

namespace DarkMyst.Expedition
{
    /// <summary>Result of one map generation pass. See <see cref="ExpeditionMapGenerator.Generate"/>.</summary>
    internal sealed class ExpeditionMapResult
    {
        public List<ExpeditionNodeState> Nodes { get; set; } = new List<ExpeditionNodeState>();
        public List<int> EntryNodeIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// Builds the whole node graph for one stage from one RNG stream, in one pass, up front.
    /// <para>
    /// Nothing here is re-rolled later: the map <see cref="ExpeditionRun.Start"/> produces for a
    /// given seed and content version is the map for the lifetime of that run, and — because it
    /// only depends on the seed and the content, never on anything the player does — the same
    /// pair reproduces the exact same map forever, which is what lets a client pre-render or
    /// cache it and a server re-derive it to check a resume.
    /// </para>
    /// </summary>
    internal static class ExpeditionMapGenerator
    {
        /// <summary>Kinds a layer can actually roll, in the one fixed order used everywhere a
        /// weighted pick has to be made — <see cref="ContentPack.Validate"/> checks against the
        /// same set so a mismatch is caught long before generation runs.</summary>
        private static readonly NodeKind[] DrawableNodeKinds =
        {
            NodeKind.Battle, NodeKind.Event, NodeKind.Treasure, NodeKind.Rest
        };

        public static ExpeditionMapResult Generate(StageData stage, DeterministicRandom rng)
        {
            var nodes = new List<ExpeditionNodeState>();
            var layerRanges = new List<LayerRange>();

            foreach (StageLayerData layer in stage.Layers)
            {
                int start = nodes.Count;
                for (int i = 0; i < layer.NodeCount; i++)
                {
                    NodeKind kind = PickNodeKind(layer, rng);
                    nodes.Add(new ExpeditionNodeState
                    {
                        Id = nodes.Count,
                        Layer = layerRanges.Count,
                        Kind = kind,
                        RefId = PickRefId(layer, kind, rng)
                    });
                }

                layerRanges.Add(new LayerRange(start, layer.NodeCount));
            }

            int bossId = nodes.Count;
            nodes.Add(new ExpeditionNodeState
            {
                Id = bossId,
                Layer = stage.Layers.Count,
                Kind = NodeKind.Boss,
                RefId = stage.BossEncounterId
            });

            for (int layerIndex = 0; layerIndex < layerRanges.Count; layerIndex++)
            {
                LayerRange current = layerRanges[layerIndex];
                LayerRange next = layerIndex + 1 < layerRanges.Count
                    ? layerRanges[layerIndex + 1]
                    : new LayerRange(bossId, 1);

                BuildEdges(nodes, current, next, rng, stage.BranchChancePerMille);
            }

            var result = new ExpeditionMapResult();
            result.Nodes = nodes;

            if (layerRanges.Count > 0)
            {
                LayerRange first = layerRanges[0];
                for (int i = 0; i < first.Count; i++)
                {
                    result.EntryNodeIds.Add(first.Start + i);
                }
            }
            else
            {
                // No authored layers at all: the boss is the only node there is.
                // ContentPack.Validate rejects this, so it is a defensive fallback, not a path
                // real content can reach.
                result.EntryNodeIds.Add(bossId);
            }

            return result;
        }

        private static NodeKind PickNodeKind(StageLayerData layer, DeterministicRandom rng)
        {
            int total = 0;
            foreach (NodeKind kind in DrawableNodeKinds)
            {
                total += WeightOf(layer, kind);
            }

            int roll = rng.NextInt(0, total);
            int cumulative = 0;
            foreach (NodeKind kind in DrawableNodeKinds)
            {
                cumulative += WeightOf(layer, kind);
                if (roll < cumulative)
                {
                    return kind;
                }
            }

            // Unreachable once ContentPack.Validate has required a positive total weight per
            // layer; kept as a hard failure rather than silently falling back to some kind.
            throw new ExpeditionException("Layer has no node kind with positive weight to draw.");
        }

        private static int WeightOf(StageLayerData layer, NodeKind kind)
        {
            int value;
            return layer.NodeWeights != null && layer.NodeWeights.TryGetValue(kind, out value) ? value : 0;
        }

        private static string PickRefId(StageLayerData layer, NodeKind kind, DeterministicRandom rng)
        {
            switch (kind)
            {
                case NodeKind.Battle:
                    return layer.EncounterIds[rng.NextInt(0, layer.EncounterIds.Count)];
                case NodeKind.Event:
                    return layer.EventIds[rng.NextInt(0, layer.EventIds.Count)];
                case NodeKind.Treasure:
                    return layer.TreasureTableId;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Wires one layer to the next so that, whichever node the player is standing on, at
        /// least one path onward always exists, and every node in the next layer is reachable
        /// from somewhere — a stage can never generate an unwinnable dead end no matter which
        /// choices are made. Two passes guarantee it in either direction, then a third pass adds
        /// the branching that turns "a path" into "a choice":
        /// <list type="number">
        /// <item><description>Forward: every source gets a primary edge, spread evenly across
        /// the next layer by integer division.</description></item>
        /// <item><description>Backward: any next-layer node the forward pass missed gets a
        /// single incoming edge, by the same even spread run in reverse.</description></item>
        /// <item><description>Branch: each source independently rolls a second edge to a
        /// neighbouring target, so more than one option is often on the table.</description></item>
        /// </list>
        /// </summary>
        private static void BuildEdges(
            List<ExpeditionNodeState> nodes,
            LayerRange current,
            LayerRange next,
            DeterministicRandom rng,
            int branchChancePerMille)
        {
            var hasIncoming = new bool[next.Count];

            for (int i = 0; i < current.Count; i++)
            {
                int primary = next.Start + (i * next.Count) / current.Count;
                AddEdge(nodes[current.Start + i], primary);
                hasIncoming[primary - next.Start] = true;
            }

            for (int j = 0; j < next.Count; j++)
            {
                if (hasIncoming[j])
                {
                    continue;
                }

                int source = current.Start + (j * current.Count) / next.Count;
                AddEdge(nodes[source], next.Start + j);
            }

            if (next.Count <= 1)
            {
                // Every layer converges on a single boss node: nothing to branch into.
                return;
            }

            for (int i = 0; i < current.Count; i++)
            {
                if (!rng.Chance(branchChancePerMille))
                {
                    continue;
                }

                int primary = next.Start + (i * next.Count) / current.Count;
                int alt = primary + 1 <= next.Start + next.Count - 1 ? primary + 1 : primary - 1;
                AddEdge(nodes[current.Start + i], alt);
            }
        }

        private static void AddEdge(ExpeditionNodeState source, int targetId)
        {
            if (!source.NextNodeIds.Contains(targetId))
            {
                source.NextNodeIds.Add(targetId);
            }
        }

        private readonly struct LayerRange
        {
            public readonly int Start;
            public readonly int Count;

            public LayerRange(int start, int count)
            {
                Start = start;
                Count = count;
            }
        }
    }
}
