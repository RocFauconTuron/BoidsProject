using UnityEngine;

public class collide : MonoBehaviour
{
    [SerializeField] Transform otherSide;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void OnTriggerEnter(Collider other)
    {
        if(other.GetComponent<Boid>().collided != this)
        {
            //Vector3 localPos = transform.InverseTransformPoint(other.transform.position);
            //other.transform.position = otherSide.TransformPoint(localPos);

            Vector3 localPos = transform.InverseTransformPoint(other.transform.position);
            // Detectar en qué eje ocurrió la colisión
            if (Mathf.Abs(localPos.x) > Mathf.Abs(localPos.y) && Mathf.Abs(localPos.x) > Mathf.Abs(localPos.z))
            {
                // Colisión en el eje X (izquierda/derecha)
                localPos.y *= -1;
                localPos.z *= -1;
                if (other.transform.position.x > 0)
                {
                    localPos.x += 2;
                }
                else
                {
                    localPos.x = +2;
                }
            }
            else if (Mathf.Abs(localPos.y) > Mathf.Abs(localPos.x) && Mathf.Abs(localPos.y) > Mathf.Abs(localPos.z))
            {
                // Colisión en el eje Y (arriba/abajo)
                localPos.z *= -1;
                localPos.x *= -1;
                if (other.transform.position.y > 0)
                {
                    localPos.y += 2;
                }
                else
                {
                    localPos.y -= 2;
                }
            }
            else
            {
                // Colisión en el eje Z (frente/atrás)
                localPos.x *= -1;
                localPos.y *= -1;
                if (other.transform.position.z > 0)
                {
                    localPos.z += 2;
                }
                else
                {
                    localPos.z -= 2;
                }
            }

            // Teletransportar al otro lado
            other.transform.position = otherSide.TransformPoint(localPos);
            other.GetComponent<Boid>().collided = otherSide.GetComponent<collide>();
        }

    }
}
