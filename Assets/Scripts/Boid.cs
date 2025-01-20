using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Boid : MonoBehaviour 
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

    public Vector3 collisiondir;

    public Vector3 accelerationPublic;

    // State
    [HideInInspector]
    public Vector3 position;
    [HideInInspector]
    public Vector3 forward;
    Vector3 velocity;

    // To update:
    Vector3 acceleration;
    [HideInInspector]
    public Vector3 avgFlockDirection;
    [HideInInspector]
    public Vector3 avgSeparationDirection;
    [HideInInspector]
    public Vector3 centreOfFlockmates;
    [HideInInspector]
    public int numPerceivedFlockmates;
    [HideInInspector]
    public Vector3 globalDirConstant;

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
        Vector3 acceleration = Vector3.zero;

        if (numPerceivedFlockmates != 0)
        {
            centreOfFlockmates /= numPerceivedFlockmates;

            Vector3 offsetToFlockmatesCentre = (centreOfFlockmates - position);
            var alignmentForce = SteerTowards(avgFlockDirection, maxSpeed) * alignWeight;
            var cohesionForce = SteerTowards(offsetToFlockmatesCentre, maxSpeed) * cohesionWeight;
            var seperationForce = SteerTowards(avgSeparationDirection, maxSpeed) * seperateWeight;


            acceleration += alignmentForce;
            acceleration += cohesionForce;
            acceleration += seperationForce;

        }
        var globalDirForce = SteerTowards(globalDirConstant, steerSpeed) * maxSteeringForce;
        acceleration += globalDirForce;

        if (IsHeadingForCollision())
        {
            Vector3 collisionAvoidDir = ObstacleRays();
            Vector3 collisionAvoidForce = SteerTowards(collisionAvoidDir,maxSpeed) * avoidCollisionWeight;
            acceleration += collisionAvoidForce;
        }

        velocity += acceleration * Time.deltaTime;
        float speed = velocity.magnitude;
        Vector3 dir = velocity / speed;
        speed = Mathf.Clamp(speed, minSpeed, maxSpeed);
        velocity = dir * speed;

        cachedTransform.position += velocity * Time.deltaTime;
        cachedTransform.forward = dir;
        position = cachedTransform.position;
        forward = dir;
        accelerationPublic = acceleration;
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

    Vector3 ObstacleRays()
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

    Vector3 SteerTowards(Vector3 vector, float speed)
    {
        Vector3 v = vector.normalized * speed - velocity;
        return Vector3.ClampMagnitude(v, maxSteerForce);
    }

}