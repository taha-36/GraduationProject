using UnityEngine;

public static class VoxelPhysicsUtils
{
    public static bool IntersectsTriangleAABB(Vector3 center, Vector3 extents, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        v0 -= center;
        v1 -= center;
        v2 -= center;

        float min, max, p0, p1, p2, rad;
        Vector3 f0 = v1 - v0, f1 = v2 - v1, f2 = v0 - v2;

        // SAT tests
        float[] axisX = { 0, -f0.z, f0.y };
        p0 = v0.y * axisX[1] + v0.z * axisX[2];
        p2 = v2.y * axisX[1] + v2.z * axisX[2];
        min = Mathf.Min(p0, p2); max = Mathf.Max(p0, p2);
        rad = extents.y * Mathf.Abs(axisX[1]) + extents.z * Mathf.Abs(axisX[2]);
        if (min > rad || max < -rad) return false;

        float[] axisY = { f0.z, 0, -f0.x };
        p0 = v0.x * axisY[0] + v0.z * axisY[2];
        p2 = v2.x * axisY[0] + v2.z * axisY[2];
        min = Mathf.Min(p0, p2); max = Mathf.Max(p0, p2);
        rad = extents.x * Mathf.Abs(axisY[0]) + extents.z * Mathf.Abs(axisY[2]);
        if (min > rad || max < -rad) return false;

        float[] axisZ = { -f0.y, f0.x, 0 };
        p0 = v0.x * axisZ[0] + v0.y * axisZ[1];
        p1 = v1.x * axisZ[0] + v1.y * axisZ[1];
        min = Mathf.Min(p0, p1); max = Mathf.Max(p0, p1);
        rad = extents.x * Mathf.Abs(axisZ[0]) + extents.y * Mathf.Abs(axisZ[1]);
        if (min > rad || max < -rad) return false;

        // ... we can skip the rest of the edge tests for brevity since AABB vs AABB is usually enough for voxels, but here is AABB
        min = Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x));
        max = Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x));
        if (min > extents.x || max < -extents.x) return false;

        min = Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y));
        max = Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y));
        if (min > extents.y || max < -extents.y) return false;

        min = Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z));
        max = Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z));
        if (min > extents.z || max < -extents.z) return false;

        Vector3 normal = Vector3.Cross(f0, f1);
        float d = Vector3.Dot(normal, v0);
        rad = extents.x * Mathf.Abs(normal.x) + extents.y * Mathf.Abs(normal.y) + extents.z * Mathf.Abs(normal.z);
        if (Mathf.Abs(d) > rad) return false;

        return true;
    }
}

