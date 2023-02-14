using KartGame.KartSystems;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;
using Random = UnityEngine.Random;
using Assets.Karting.Scripts.AI;
using System.Collections;
using System.Collections.Generic;
using System;
using static Assets.Karting.Scripts.AI.ListOfFloatExtensions;
using System.Linq;
using UnityEngine.SceneManagement;

namespace KartGame.AI
{
    /// <summary>
    /// Sensors hold information such as the position of rotation of the origin of the raycast and its hit threshold
    /// to consider a "crash".
    /// </summary>
    [System.Serializable]
    public struct Sensor
    {
        public Transform Transform;
        public float RayDistance;
        public float HitValidationDistance;
    }

    /// <summary>
    /// We only want certain behaviours when the agent runs.
    /// Training would allow certain functions such as OnAgentReset() be called and execute, while Inferencing will
    /// assume that the agent will continuously run and not reset.
    /// </summary>
    public enum AgentMode
    {
        Training,
        Inferencing
    }

    /// <summary>
    /// The KartAgent will drive the inputs for the KartController.
    /// </summary>
    public class KartAgent : MonoBehaviour, IInput
    {
#region Training Modes
        [Tooltip("Are we training the agent or is the agent production ready?")]
        public AgentMode Mode = AgentMode.Training;
        [Tooltip("What is the initial checkpoint the agent will go to? This value is only for inferencing.")]
        public ushort InitCheckpointIndex;

#endregion

#region Senses
        [Header("Observation Params")]
        [Tooltip("What objects should the raycasts hit and detect?")]
        public LayerMask Mask;
        [Tooltip("Sensors contain ray information to sense out the world, you can have as many sensors as you need.")]
        public Sensor[] Sensors;
        [Header("Checkpoints"), Tooltip("What are the series of checkpoints for the agent to seek and pass through?")]
        public Collider[] Colliders;
        [Tooltip("What layer are the checkpoints on? This should be an exclusive layer for the agent to use.")]
        public LayerMask CheckpointMask;

        [Space]
        [Tooltip("Would the agent need a custom transform to be able to raycast and hit the track? " +
            "If not assigned, then the root transform will be used.")]
        public Transform AgentSensorTransform;
#endregion

#region Rewards
        [Header("Rewards"), Tooltip("What penatly is given when the agent crashes?")]
        public float HitPenalty = -1f;
        [Header("Rewards"), Tooltip("Slow penalty")]
        public float SlowPenalty = -1f;
        [Tooltip("How much reward is given when the agent successfully passes the checkpoints?")]
        public float PassCheckpointReward;
        [Tooltip("Should typically be a small value, but we reward the agent for moving in the right direction.")]
        public float TowardsCheckpointReward;
        [Tooltip("Typically if the agent moves faster, we want to reward it for finishing the track quickly.")]
        public float SpeedReward;
        [Tooltip("Reward the agent when it keeps accelerating")]
        public float AccelerationReward;
        #endregion

        #region ResetParams
        [Header("Inference Reset Params")]
        [Tooltip("What is the unique mask that the agent should detect when it falls out of the track?")]
        public LayerMask OutOfBoundsMask;
        [Tooltip("What are the layers we want to detect for the track and the ground?")]
        public LayerMask TrackMask;
        [Tooltip("How far should the ray be when casted? For larger karts - this value should be larger too.")]
        public float GroundCastDistance;
#endregion

#region Debugging
        [Header("Debug Option")] [Tooltip("Should we visualize the rays that the agent draws?")]
        public bool ShowRaycasts;
#endregion

        ArcadeKart m_Kart;
        bool m_Acceleration;
        bool m_Brake;
        float m_Steering;
        int m_CheckpointIndex;

        bool m_EndEpisode;
        float m_LastAccumulatedReward;
        TfUdpClient Client;
        Coroutine ProcessTfCoroutine;
        List<float> CurrentState;
        float Reward;
        public float RandomMoveProbability;
        public int? Decision;

        int Sent = 0;
        int Received = 0;

        public int StepsBetweenUpdate = 1;

        void Awake()
        {
            Reward = 0f;
            m_Kart = GetComponent<ArcadeKart>();
            if (AgentSensorTransform == null) AgentSensorTransform = transform;
            Client = new TfUdpClient("127.0.0.1", 5004);
            ProcessTfCoroutine = StartCoroutine(ProcessTfData());
        }

        void Start()
        {
            // If the agent is training, then at the start of the simulation, pick a random checkpoint to train the agent.
            OnEpisodeBegin();

            if (Mode == AgentMode.Inferencing) m_CheckpointIndex = InitCheckpointIndex;

            Mode = AgentMode.Training;
        }

        void Update()
        {
            if (m_EndEpisode)
            {
                m_EndEpisode = false;
                //EndEpisode();
                OnEpisodeBegin();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SceneManager.LoadScene(0);
            }
        }

        void FixedUpdate()
        {
            CurrentState = CollectObservations();
            CollectRewards();
        }

        void LateUpdate()
        {
            if (ShowRaycasts)
                Debug.DrawRay(transform.position, Vector3.down * GroundCastDistance, Color.cyan);

            // We want to place the agent back on the track if the agent happens to launch itself outside of the track.
            if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out var hit, GroundCastDistance, TrackMask)
                && ((1 << hit.collider.gameObject.layer) & OutOfBoundsMask) > 0)
            {
                // Reset the agent back to its last known agent checkpoint
                var checkpoint = Colliders[m_CheckpointIndex].transform;
                transform.localRotation = checkpoint.rotation;
                transform.position = checkpoint.position;
                m_Kart.Rigidbody.velocity = default;
                m_Steering = 0f;
                m_Acceleration = m_Brake = false;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            var maskedValue = 1 << other.gameObject.layer;
            var triggered = maskedValue & CheckpointMask;

            FindCheckpointIndex(other, out var index);

            // Ensure that the agent touched the checkpoint and the new index is greater than the m_CheckpointIndex.
            if (triggered > 0 && index > m_CheckpointIndex || index == 0 && m_CheckpointIndex == Colliders.Length - 1)
            {
                Reward += PassCheckpointReward;

                Debug.Log($"PassCPR {PassCheckpointReward}");
                m_CheckpointIndex = index;
            }
        }

        void FindCheckpointIndex(Collider checkPoint, out int index)
        {
            for (int i = 0; i < Colliders.Length; i++)
            {
                if (Colliders[i].GetInstanceID() == checkPoint.GetInstanceID())
                {
                    index = i;
                    return;
                }
            }
            index = -1;
        }

        public IEnumerator ProcessTfData()
        {
            while (true)
            {
                if (CurrentState != null)
                {
                    var observationBytes = CurrentState.ToBytes();

                    if (observationBytes != null && observationBytes.Count > 0)
                    {
                        Reward = 0f;

                        Client.Send(observationBytes.ToArray());
                        Sent++;

                        yield return new WaitWhile(Client.CantReceive);

                        var tfResult = Client.Receive();
                        ProcessTfResponse(tfResult);
                        Received++;
                        Debug.Log("Decision: " + string.Join(", ", tfResult.DecisionAndValue.Select(x => x.Item2)));
                        Debug.Log($"Sent: {Sent} Received: {Received}");
                    }

                    for (int i = 0; i < StepsBetweenUpdate; i++)
                    {
                        yield return new WaitForFixedUpdate();
                    }

                    observationBytes = CurrentState.ToBytes();

                    if (Decision != null)
                    {
                        observationBytes.AddRange(BitConverter.GetBytes((int)Decision));
                        observationBytes.AddRange(BitConverter.GetBytes(Reward));
                    }

                    Debug.Log(Reward);

                    if (observationBytes != null && observationBytes.Count > 0)
                    {
                        Client.Send(observationBytes.ToArray());
                        Sent++;
                    }
                }

                yield return null;
            }
        }

        public void ProcessTfResponse(NamedFloats tfResult)
        {
            var rand = Random.Range(0f, 1f);

            var (_class, value) = tfResult.DecisionAndValue.FirstOrDefault();

            if (rand < RandomMoveProbability)
            {
                _class = (NamedFloats.VariableClass)Random.Range(0, 5);
            }

            m_Steering = 0;

            if (_class == NamedFloats.VariableClass.LeftTurnAndAcc || _class == NamedFloats.VariableClass.LeftTurnAndBreak)
            {
                m_Steering -= 0.6f;
            }
            else if (_class == NamedFloats.VariableClass.RightTurnAndAcc || _class == NamedFloats.VariableClass.RightTurnAndBreak)
            {
                m_Steering += 0.6f;
            }

            m_Brake = false;
            m_Acceleration = false;

            if (_class == NamedFloats.VariableClass.LeftTurnAndBreak || _class == NamedFloats.VariableClass.RightTurnAndBreak || _class == NamedFloats.VariableClass.NoTurnAndBreak)
            {
                m_Brake = true;
            }
            else if (_class == NamedFloats.VariableClass.LeftTurnAndAcc || _class == NamedFloats.VariableClass.RightTurnAndAcc || _class == NamedFloats.VariableClass.NoTurnAndAcc)
            {
                m_Acceleration = true;
            }

            Decision = (int)_class;

            Debug.Log($"Steer: {m_Steering}, Brake: {m_Brake}, Acceleration: {m_Acceleration}");
        }

        float Sign(float value)
        {
            if (value > 0)
            {
                return 1;
            } 
            if (value < 0)
            {
                return -1;
            }
            return 0;
        }

        public List<float> CollectObservations()
        {
            var observations = new List<float>();

            observations.Add(m_Kart.LocalSpeed());

            // Add an observation for direction of the agent to the next checkpoint.
            var next = (m_CheckpointIndex + 1) % Colliders.Length;
            var nextCollider = Colliders[next];
            if (nextCollider == null)
                return null;

            var direction = (nextCollider.transform.position - m_Kart.transform.position).normalized;
            observations.Add(Vector3.Dot(m_Kart.Rigidbody.velocity.normalized, direction));

            if (ShowRaycasts)
                Debug.DrawLine(AgentSensorTransform.position, nextCollider.transform.position, Color.magenta);

            for (var i = 0; i < Sensors.Length; i++)
            {
                var current = Sensors[i];
                var xform = current.Transform;
                var hit = Physics.Raycast(AgentSensorTransform.position, xform.forward, out var hitInfo,
                    current.RayDistance, Mask, QueryTriggerInteraction.Ignore);

                if (ShowRaycasts)
                {
                    Debug.DrawRay(AgentSensorTransform.position, xform.forward * current.RayDistance, Color.green);
                    Debug.DrawRay(AgentSensorTransform.position, xform.forward * current.HitValidationDistance, 
                        Color.red);

                    if (hit && hitInfo.distance < current.HitValidationDistance)
                    {
                        Debug.DrawRay(hitInfo.point, Vector3.up * 3.0f, Color.blue);
                    }
                }

                if (hit)
                {
                    if (hitInfo.distance < current.HitValidationDistance)
                    {
                        Reward += HitPenalty;
                        Debug.Log($"Hitpen {HitPenalty}");
                       // m_EndEpisode = true;
                    }
                }

                observations.Add(hit ? hitInfo.distance : current.RayDistance);
            }

            observations.Add(Convert.ToSingle(m_Acceleration));

            return observations;
        }

        public void CollectRewards()
        {
            // Find the next checkpoint when registering the current checkpoint that the agent has passed.
            var next = (m_CheckpointIndex + 1) % Colliders.Length;
            var nextCollider = Colliders[next];
            var direction = (nextCollider.transform.position - m_Kart.transform.position).normalized;

            var localSpeed = m_Kart.LocalSpeed();

            Debug.Log("Speed" + localSpeed);

            if (Mathf.Abs(localSpeed) < 0.4f)
            {
                Reward += SlowPenalty * (1 - Mathf.Abs(localSpeed)) * 0.5f;
            }

            /*if (localSpeed < 1f)
            {
                Reward += SlowPenalty * (1 - Mathf.Abs(localSpeed));
            }*/

            var reward = Vector3.Dot(m_Kart.Rigidbody.velocity.normalized, direction);
            Debug.Log("rexx" + $"{reward * TowardsCheckpointReward} " +
                $"{(m_Acceleration && !m_Brake ? 1.0f : 0.0f) * AccelerationReward} " +
                $"{m_Kart.LocalSpeed() * SpeedReward}");
            Reward += reward * TowardsCheckpointReward;
            Reward += (m_Acceleration && !m_Brake ? 1.0f : 0.0f) * AccelerationReward;
            Reward += m_Kart.LocalSpeed() * SpeedReward;  
        }

        public void OnEpisodeBegin()
        {
            switch (Mode)
            {
                case AgentMode.Training:
                    m_CheckpointIndex = Random.Range(0, Colliders.Length - 1);
                    var collider = Colliders[m_CheckpointIndex];
                    transform.localRotation = collider.transform.rotation;
                    transform.position = collider.transform.position;
                    m_Kart.Rigidbody.velocity = default;
                    m_Acceleration = false;
                    m_Brake = false;
                    m_Steering = 0f;
                    break;
                default:
                    break;
            }
        }

        void InterpretDiscreteActions(ActionBuffers actions)
        {
            m_Steering = actions.DiscreteActions[0] - 1f;
            m_Acceleration = actions.DiscreteActions[1] >= 1.0f;
            m_Brake = actions.DiscreteActions[1] < 1.0f;
        }

        public InputData GenerateInput()
        {
            return new InputData
            {
                Accelerate = m_Acceleration,
                Brake = m_Brake,
                TurnInput = m_Steering
            };
        }
    }
}
