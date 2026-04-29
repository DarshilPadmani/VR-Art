using System.Collections.Generic;
using UnityEngine;

public static class BrushMathUtils
{
    // A simple moving average filter to remove hand jitter.
    public static Vector3 GetSmoothedPosition(List<Vector3> pointHistory, Vector3 newPoint, int windowSize)
    {
        pointHistory.Add(newPoint);
        if (pointHistory.Count > windowSize)
        {
            pointHistory.RemoveAt(0);
        }

        Vector3 average = Vector3.zero;
        foreach (var p in pointHistory)
        {
            average += p;
        }

        return average / pointHistory.Count;
    }
}