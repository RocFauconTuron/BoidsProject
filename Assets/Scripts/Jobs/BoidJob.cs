using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine.Jobs;
using Unity.Collections;

[BurstCompile]
public class BoidJob : MonoBehaviour
{
    // Settings
    public float minSpeed = 2;
    public float maxSpeed = 5;
    public float perceptionRadius = 2.5f;
    public float avoidanceRadius = 1;
    public float maxSteerForce = 3;

    public float alignWeight = 1;
    public float cohesionWeight = 1;
    public float seperateWeight = 1;

    public float maxSteeringForce = 1f; // Fuerza máxima de dirección
    public float wanderJitter = 0.2f; // Cantidad de variación aleatoria por frame
    public float timeMinWander = 1;
    public float timeMaxWander = 5;
    public float steerSpeed = 2;

    [Header("Collisions")]
    public LayerMask obstacleMask;
    public float boundsRadius = .27f;
    public float avoidCollisionWeight = 10;
    public float collisionAvoidDst = 5;




    // State
    [HideInInspector]
    public float3 position;
    [HideInInspector]
    public float3 forward;
    float3 velocity;

    // To update:
    float3 acceleration;
    [HideInInspector]
    public float3 avgFlockDirection;
    [HideInInspector]
    public float3 avgSeparationDirection;
    [HideInInspector]
    public float3 centreOfFlockmates;
    [HideInInspector]
    public int numPerceivedFlockmates;
    [HideInInspector]
    public float3 globalDirConstant;

    // Cached
    Transform cachedTransform;

    void Awake()
    {
        cachedTransform = transform;
    }

    public void Initialize()
    {
        position = cachedTransform.position;
        forward = cachedTransform.forward;

        float startSpeed = (minSpeed + maxSpeed) / 2;
        velocity = transform.forward * startSpeed;
    }
    public void UpdateBoid()
    {
        float3 dir = float3.zero;
        boidJobUpdate boidJob = new boidJobUpdate()
        {
            avgFlockDirection = avgFlockDirection,
            avgSeparationDirection = avgSeparationDirection,
            centreOfFlockmates = centreOfFlockmates,
            numPerceivedFlockmates = numPerceivedFlockmates,
            globalDirConstant = globalDirConstant,
            position = position,
            forward = forward,
            minSpeed = minSpeed,
            maxSpeed = maxSpeed,
            maxSteerForce = maxSteerForce,

            alignWeight = alignWeight,
            cohesionWeight = cohesionWeight,
            seperateWeight = seperateWeight,

            maxSteeringForce = maxSteeringForce,
            steerSpeed = steerSpeed,
            deltaTime = Time.deltaTime,

            velocity = velocity,
            dir = dir
        };

        JobHandle jobHandle = boidJob.Schedule();
        jobHandle.Complete();

        if (IsHeadingForCollision())
        {
            float3 collisionAvoidDir = ObstacleRays();
            float3 collisionAvoidForce = SteerTowards(collisionAvoidDir, maxSpeed) * avoidCollisionWeight;
            acceleration += collisionAvoidForce;
        }

        velocity += acceleration * Time.deltaTime;
        float speed = math.length(velocity);
        dir = velocity / speed;
        speed = math.clamp(speed, minSpeed, maxSpeed);
        velocity = dir * speed;

        cachedTransform.position += (Vector3)velocity * Time.deltaTime;
        cachedTransform.forward = dir;
        position = cachedTransform.position;
        forward = dir;
    }

    bool IsHeadingForCollision()
    {
        RaycastHit hit;
        if (Physics.SphereCast(position, boundsRadius, forward, out hit, collisionAvoidDst, obstacleMask))
        {
            return true;
        }
        else { }
        return false;
    }

    float3 ObstacleRays()
    {
        Vector3[] rayDirections = BoidHelper.directions;

        for (int i = 0; i < rayDirections.Length; i++)
        {
            Vector3 dir = cachedTransform.TransformDirection(rayDirections[i]);
            Ray ray = new Ray(position, dir);
            if (!Physics.SphereCast(ray, boundsRadius, collisionAvoidDst, obstacleMask))
            {
                return dir;
            }
        }

        return forward;
    }
    float3 SteerTowards(float3 vector, float speed)
    {
        float3 v = math.normalize(vector) * speed - velocity;
        return math.clamp(v, 0, maxSteerForce);
    }


}
[BurstCompile]
struct boidJobUpdate : IJob
{
    [ReadOnly] public float3 avgFlockDirection;
    [ReadOnly] public float3 avgSeparationDirection;
    [ReadOnly] public float3 centreOfFlockmates;
    [ReadOnly] public int numPerceivedFlockmates;
    [ReadOnly]public float3 globalDirConstant;
    [ReadOnly] public float3 position;
    [ReadOnly] public float3 forward;

    [ReadOnly] public float minSpeed;
    [ReadOnly] public float maxSpeed;
    [ReadOnly] public float maxSteerForce;

    [ReadOnly] public float alignWeight;
    [ReadOnly] public float cohesionWeight;
    [ReadOnly] public float seperateWeight;

    [ReadOnly] public float maxSteeringForce;
    [ReadOnly] public float steerSpeed;
    [ReadOnly] public float deltaTime;

    public float3 velocity;
    public float3 dir;

    float3 acceleration;
    public void Execute()
    {
        float3 acceleration = Vector3.zero;

        if (numPerceivedFlockmates != 0)
        {
            centreOfFlockmates /= numPerceivedFlockmates;

            float3 offsetToFlockmatesCentre = (centreOfFlockmates - position);
            float3 alignmentForce = SteerTowards(avgFlockDirection, maxSpeed) * alignWeight;
            float3 cohesionForce = SteerTowards(offsetToFlockmatesCentre, maxSpeed) * cohesionWeight;
            float3 seperationForce = SteerTowards(avgSeparationDirection, maxSpeed) * seperateWeight;


            acceleration += alignmentForce;
            acceleration += cohesionForce;
            acceleration += seperationForce;

        }
        float3 globalDirForce = SteerTowards(globalDirConstant, steerSpeed) * maxSteeringForce;
        acceleration += globalDirForce;
    }
    float3 SteerTowards(float3 vector, float speed)
    {
        float3 v = math.normalize(vector) * speed - velocity;
        return math.clamp(v, 0, maxSteerForce);
    }
}
