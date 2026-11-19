using System;
using System.Reflection;
using SnakeClub.Core;

public static class StrengthContactCombinationRegression
{
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Set(PveFixedWorld world, string name, int value) =>
        typeof(PveFixedWorld).GetProperty(name, BindingFlags.Public | BindingFlags.Instance).GetSetMethod(true).Invoke(world, new object[] { value });

    private static PveFixedWorld Trace(int seed, bool firstRun, int until)
    {
        var world = new PveFixedWorld(seed, firstRun, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
            localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true,
            localMultiBite: true, localHeadTieAfterBite: true);
        byte heading = 0;
        while (!world.Finished && world.Tick < until)
        {
            if (world.Tick % 8 == 0)
            {
                var player = world.Fighters[0].motion; long nearest = long.MaxValue; int dx = 0, dy = 0;
                foreach (var opponent in world.Fighters)
                {
                    if (opponent.id == "you" || !opponent.alive || opponent.motion.Length <= PveFixedMotion.MinimumLength) continue;
                    var tail = opponent.motion.Segments[opponent.motion.Length - 1];
                    int x = tail.x - player.X, y = tail.y - player.Y; long distance = (long)x * x + (long)y * y;
                    if (distance < nearest) { nearest = distance; dx = x; dy = y; }
                }
                if (nearest < long.MaxValue) heading = (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
            }
            world.Step(heading, false, true);
        }
        return world;
    }

    public static void Run()
    {
        var world = Trace(10, false, 147);
        Require(world.Tick == 147 && world.LastPlayerAttackTick == 147 && world.LastPlayerAttackCount == 3 && world.LastPlayerHeadTieTick == 147,
            "seed 10 did not produce simultaneous real bite and head separation");
        Require(StrengthContactNotice.Text(world, 147, out bool warn) == "吃掉身体 3 节 · 正面弹开" && !warn,
            "real bite and head separation were not both described");
        var injury = Trace(803, true, 204);
        Require(injury.Tick == 204 && injury.LastPlayerDamageTick == 204 && injury.LastPlayerDamageCount == 3 && injury.LastPlayerHeadTieTick == 204,
            "seed 803 did not produce simultaneous real damage and head separation");
        Require(StrengthContactNotice.Text(injury, 204, out warn) == "身体被吃掉 3 节 · 正面弹开" && warn,
            "real damage and head separation were not both described");
        // Isolate independent event times and counts; no match state is advanced.
        Set(world, "LastPlayerAttackTick", 100); Set(world, "LastPlayerAttackCount", 2);
        Set(world, "LastPlayerDamageTick", -1); Set(world, "LastPlayerBlockedTick", -1); Set(world, "LastPlayerHeadTieTick", 100);
        string Text(int tick) => StrengthContactNotice.Text(world, tick, out _);
        Require(Text(100) == "吃掉身体 2 节 · 正面弹开", "attack and tie");
        Set(world, "LastPlayerDamageTick", 100); Set(world, "LastPlayerDamageCount", 3);
        Require(Text(100) == "互相吞食 · 吃 2 节 / 失 3 节 · 正面弹开", "mutual bite and tie");
        Set(world, "LastPlayerBlockedTick", 100);
        Require(Text(100) == "互吃 +2/-3 · 遇强弹开 · 正面弹开", "mutual bite, blocked body and tie");
        Set(world, "LastPlayerAttackTick", -1);
        Require(Text(100) == "身体被吃 3 节 · 遇强弹开 · 正面弹开", "damage, blocked body and tie");
        Set(world, "LastPlayerBlockedTick", -1);
        Require(Text(100) == "身体被吃掉 3 节 · 正面弹开", "damage and tie");
        Set(world, "LastPlayerDamageTick", -1); Set(world, "LastPlayerBlockedTick", 100);
        Require(Text(100) == "强度不足 · 试试更弱的部位 · 正面弹开", "blocked body and tie");
        Set(world, "LastPlayerBlockedTick", -1);
        Require(Text(100) == "势均力敌 · 双方弹开", "tie-only text changed");
        Set(world, "LastPlayerAttackTick", 101);
        Require(Text(101) == "吃掉身体 2 节！", "older tie incorrectly attached to newer attack");
        Set(world, "LastPlayerAttackTick", 100); Set(world, "LastPlayerHeadTieTick", 101);
        Require(Text(101) == "势均力敌 · 双方弹开", "older attack incorrectly attached to newer tie");
        Require(Text(121) == "", "expired contact remained visible");
        Require(StrengthContactNotice.Text(null, 101, out _) == "", "null world displayed a contact");
        var legacy = new PveFixedWorld(1, false, null);
        Set(legacy, "LastPlayerHeadTieTick", 100);
        Require(StrengthContactNotice.Text(legacy, 100, out _) == "", "legacy mode displayed strength contact");
    }
}
