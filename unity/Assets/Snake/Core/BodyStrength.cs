using System;

namespace SnakeClub.Core
{
    // Candidate v0.0.3.3 math only. It is not yet connected to match rules,
    // progression, rewards, the leaderboard or any platform randomness.
    public struct StrengthInputs
    {
        public int basePower, matchLevel, length, hardness;
        public int speed, baseSpeed;
        public bool boosting;
        // Basis points: 1000 is neutral. The source and duration of these
        // factors belong to a future versioned match state, not this calculator.
        public int randomFactor, operationFactor;
    }

    public struct StrengthValues
    {
        public int attack, defense, baseStrength, bodyFactor, speedFactor, effectFactor;
    }

    public static class BodyStrength
    {
        public const string ExperimentVersion = "body-strength-experiment-1";
        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        // Segment zero is the head and length - 1 the tail. Invalid geometry
        // or a nonpositive reference speed is rejected rather than guessed.
        public static bool TryCalculate(StrengthInputs input, int segment, out StrengthValues result)
        {
            result = default;
            if (input.length < 1 || input.length > 80 || segment < 0 || segment >= input.length ||
                input.speed < 0 || input.baseSpeed <= 0) return false;
            int p = Clamp(input.basePower, 0, 300);
            int level = Clamp(input.matchLevel, 0, 20);
            int hardness = Clamp(input.hardness, 0, 300);
            int strength = Clamp(p + 3 * level + 3 * Math.Max(0, input.length - 16) / 2, 100, 300);
            int progress = (int)((long)1000 * segment / Math.Max(1, input.length - 1));
            int body = 1000 - 450 * progress / 1000;
            long speedRatio = Math.Max(700L, Math.Min(1500L, (long)input.speed * 1000 / input.baseSpeed));
            int speed = input.boosting ? 1000 + (int)Math.Min(120L, Math.Max(0L, speedRatio - 1000) * 120 / 500) : 1000;
            int random = Clamp(input.randomFactor, 850, 1150);
            int operation = Clamp(input.operationFactor, 900, 1200);
            int effect = Clamp(random * operation / 1000, 800, 1250);
            int attack = strength * speed / 1000 * effect / 1000;
            int defense = (strength * body / 1000 + strength * hardness / 1000) * effect / 1000;
            result = new StrengthValues { attack = attack, defense = defense, baseStrength = strength,
                bodyFactor = body, speedFactor = speed, effectFactor = effect };
            return true;
        }

        // The strict 5% boundary leaves a nonlethal tie band. Actual contact,
        // protection, body loss and head-on outcomes are not implemented here.
        public static bool CanEat(int attack, int defense)
            => attack >= 0 && defense >= 0 && (long)attack * 100 > (long)defense * 105;
    }
}
