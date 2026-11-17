using UnityEngine;
using System.Collections.Generic;
// heights of the table mesh vertices near the top (to know the top slab thickness)
public class TableInfo
{
    public static string Execute()
    {
        var t = GameObject.Find("Table"); if (!t) return "no Table";
        var r = t.GetComponent<Renderer>(); var mf = t.GetComponent<MeshFilter>();
        var ys = new SortedSet<float>();
        foreach (var v in mf.sharedMesh.vertices) ys.Add(Mathf.Round(t.transform.TransformPoint(v).y * 1000f) / 1000f);
        var top = new List<string>(); int n = 0;
        foreach (var y in ys.Reverse()) { top.Add(y.ToString("0.000")); if (++n > 8) break; }
        return "bounds " + r.bounds.min.ToString("F3") + " .. " + r.bounds.max.ToString("F3") + " | highest vertex heights: " + string.Join(", ", top);
    }
}
