using System;
using System.Collections;
using System.Collections.Generic;
using DarkMyst.Combat.Model;
using UnityEngine;

namespace DarkMyst.Client.Battle
{
    /// <summary>
    /// Plays a finished <see cref="BattleResult"/> back as a timed stream of events.
    /// <para>
    /// The battle is already decided before this runs. Speeding up, slowing down, skipping to
    /// the end or replaying it later cannot change who won — which is the whole reason the
    /// simulator and the presentation are separate assemblies. The view layer subscribes to
    /// <see cref="OnEvent"/> and animates; if it drops an event the outcome is still correct.
    /// </para>
    /// </summary>
    public sealed class BattleReplay : MonoBehaviour
    {
        [Tooltip("Playback speed. Purely cosmetic: it cannot change the result.")]
        [Range(0.25f, 8f)]
        [SerializeField] private float _speed = 1f;

        [Tooltip("Seconds each event kind holds the screen at speed 1.")]
        [SerializeField] private float _actionBeat = 0.45f;
        [SerializeField] private float _minorBeat = 0.12f;
        [SerializeField] private float _roundBeat = 0.6f;

        private Coroutine _playback;

        /// <summary>Raised once per event, in order.</summary>
        public event Action<BattleEvent> OnEvent;

        /// <summary>Raised when playback reaches the end of the log.</summary>
        public event Action<BattleResult> OnFinished;

        public BattleResult Result { get; private set; }

        public bool IsPlaying => _playback != null;

        public float Speed
        {
            get => _speed;
            set => _speed = Mathf.Clamp(value, 0.25f, 8f);
        }

        public void Play(BattleResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            Stop();
            Result = result;
            _playback = StartCoroutine(PlayRoutine(result));
        }

        public void Stop()
        {
            if (_playback != null)
            {
                StopCoroutine(_playback);
                _playback = null;
            }
        }

        /// <summary>
        /// Fires every remaining event immediately and finishes. Used by the "skip" button and
        /// by auto-repeat farming, where nobody is watching the animation.
        /// </summary>
        public void SkipToEnd()
        {
            if (Result == null)
            {
                return;
            }

            BattleResult result = Result;
            int from = _delivered;
            Stop();

            for (int i = from; i < result.Events.Count; i++)
            {
                OnEvent?.Invoke(result.Events[i]);
            }

            _delivered = result.Events.Count;
            OnFinished?.Invoke(result);
        }

        private int _delivered;

        private IEnumerator PlayRoutine(BattleResult result)
        {
            _delivered = 0;

            foreach (BattleEvent evt in result.Events)
            {
                OnEvent?.Invoke(evt);
                _delivered++;

                float beat = BeatFor(evt.Kind);
                if (beat > 0f)
                {
                    yield return new WaitForSeconds(beat / Mathf.Max(_speed, 0.01f));
                }
            }

            _playback = null;
            OnFinished?.Invoke(result);
        }

        private float BeatFor(BattleEventKind kind)
        {
            switch (kind)
            {
                case BattleEventKind.SkillActivated:
                case BattleEventKind.UnitDowned:
                case BattleEventKind.UnitRevived:
                    return _actionBeat;

                case BattleEventKind.Damaged:
                case BattleEventKind.Healed:
                case BattleEventKind.ShieldAbsorbed:
                case BattleEventKind.StatusApplied:
                case BattleEventKind.StatusTicked:
                case BattleEventKind.TurnSkipped:
                    return _minorBeat;

                case BattleEventKind.RoundStarted:
                case BattleEventKind.BattleEnded:
                    return _roundBeat;

                // Bookkeeping the player never needs to wait for.
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Convenience for a view that wants the state of every unit as the log plays, without
        /// each view component tracking HP itself.
        /// </summary>
        public static Dictionary<UnitRef, int> TrackHp(BattleResult result, int upToSequence)
        {
            var hp = new Dictionary<UnitRef, int>();
            foreach (UnitSnapshot unit in result.FinalUnits)
            {
                hp[unit.Ref] = unit.MaxHp;
            }

            foreach (BattleEvent evt in result.Events)
            {
                if (evt.Sequence > upToSequence)
                {
                    break;
                }

                if (evt.Target.HasValue && evt.TargetHpAfter >= 0)
                {
                    hp[evt.Target.Value] = evt.TargetHpAfter;
                }
            }

            return hp;
        }
    }
}
