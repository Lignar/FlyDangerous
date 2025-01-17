using System.Collections;
using System.Collections.Generic;
using Core;
using UnityEngine;

public class Ring : MonoBehaviour
{
    
    [SerializeField] float rotationAmount;

    private Transform _transform;
        
    // Start is called before the first frame update
    void Start() {
        _transform = transform;
        if (rotationAmount == 0) {
            rotationAmount = Random.Range(-0.5f, 0.5f);
        }
    }

    // Update is called once per frame
    void FixedUpdate() {
        _transform.RotateAround(_transform.position, _transform.forward, rotationAmount);
    }
}
