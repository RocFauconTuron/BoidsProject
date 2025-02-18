using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] Transform targetRotate;
    [SerializeField] float cameraRotateSens;
    [SerializeField] float moveSpeed;
    [SerializeField] float zoomSpeed;
    float rotX = 0;
    float rotY = 0;
    Vector3 currentRot;
    Vector3 smoothVel = Vector3.zero;
    [SerializeField] float smoothTimeRot;
    [SerializeField] float smoothTimeMove;

    [SerializeField] float minZoom;
    [SerializeField] float maxZoom;

    float posX;
    float posY;

    private void Awake()
    {
        currentRot = targetRotate.localEulerAngles;
        rotX = targetRotate.localEulerAngles.y;
        rotY = targetRotate.localEulerAngles.x;
        posX = targetRotate.position.x;
        posY = targetRotate.position.z;
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    void Update()
    {
        if (Input.GetMouseButton(1))
        {
            rotX += Input.GetAxis("Mouse X") * cameraRotateSens;
            rotY -= Input.GetAxis("Mouse Y") * cameraRotateSens;

            rotY = Mathf.Clamp(rotY, 0, 70);

            Vector3 nextRot = new Vector3(rotY, rotX);
            currentRot = Vector3.SmoothDamp(currentRot, nextRot, ref smoothVel, smoothTimeRot);
            targetRotate.localEulerAngles = currentRot;
        }        
        if (Input.GetMouseButton(2))
        {
            posX = Input.GetAxis("Mouse X");
            posY = Input.GetAxis("Mouse Y");
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            // Normalizar los vectores forward y right para que no afecten la velocidad de movimiento
            forward.y = 0;
            right.y = 0;
            forward.Normalize();
            right.Normalize();

            // Calcular la dirección deseada en función de la entrada del usuario
            Vector3 desiredMoveDirection = (forward * -posY + right * -posX).normalized;

            // Mover el punto de pivote en la dirección deseada
            targetRotate.position += desiredMoveDirection * moveSpeed * Time.deltaTime;
        }

        //ZOOM
        float scrollInput = Input.mouseScrollDelta.y;
        if (scrollInput != 0.0f)
        {
            // Calcular la nueva distancia
            float distance = Vector3.Distance(transform.position, targetRotate.position);
            distance -= scrollInput * zoomSpeed * Time.deltaTime;
            distance = Mathf.Clamp(distance, minZoom, maxZoom);

            // Mover la cámara a la nueva posición
            Vector3 direction = (transform.position - targetRotate.position).normalized;
            transform.position = targetRotate.position + direction * distance;
        }

    }
}
