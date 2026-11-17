using System;
using System.IO;
using SnakeClub.Tests;
using UnityEngine;

namespace SnakeClub.Tests.Editor
{
    public static class PveVectorUnity
    {
        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("SNAKE_PVE_VECTOR_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new Exception("Missing vector output path");
            string vector = PveReplayVector.Compute();
            File.WriteAllText(output, vector + "\n");
            Debug.Log("PVE_VECTOR " + vector);
        }
    }
}
