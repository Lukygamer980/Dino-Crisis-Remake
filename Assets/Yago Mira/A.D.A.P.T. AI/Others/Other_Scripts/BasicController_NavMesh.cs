using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ADAPT
{
    public class BasicController_NavMesh : MonoBehaviour
    {
        public Transform destination;
        private NavMeshAgent agent;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        private void Update()
        {
            agent.destination = destination.position;
        }
    }
}