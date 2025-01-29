using Unity.Mathematics;
using System;

public struct BoidCurrentData
{
    public float3 position;
    public float3 direction;
    public float3 globalDir;

    public float3 flockHeading;
    public float3 flockCentre;
    public float3 flockSeparation;
    public int numNeightbors;
}
public struct BoidConstantData
{
    public float3 avgFlockDirection;
    public float3 avgSeparationDirection;
    public float3 centreOfFlockmates;
    public int numPerceivedFlockmates;
    public float3 globalDirConstant;
    public float3 position;
    public float3 forward;
    public float3 collisionAvoidDir;
    public bool hasFoundCollision;
    public float3 velocity;
    public quaternion rotation;
}
public struct BoidSettingsData
{
    // Settings
    public float minSpeed;
    public float maxSpeed;
    public float maxSteerForce;

    public float alignWeight;
    public float cohesionWeight;
    public float seperateWeight;

    public float maxSteeringForce;
    public float steerSpeed;
    public float avoidCollisionWeight;
}

public struct HashAndIndex : IComparable<HashAndIndex>
{
    public int Hash;
    public int Index;

    public int CompareTo(HashAndIndex other)
    {
        return Hash.CompareTo(other.Hash);
    }
}