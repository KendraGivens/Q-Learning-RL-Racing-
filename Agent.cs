using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Linq;

//Interfaces with the server 
//
public class Agent : MonoBehaviour
{
    public bool AI_Controlled = true;

    protected Model model = new Model();
    public CarController2 agent;

    System.Random random = new Random();

    //States
    List<float> previous_state = new List<float>();
    List<float> current_state = new List<float>();

    List<float> previous_predicted_actions = new List<float>();
    float previous_action_value = 0f;
    int previous_action_index = 0;

    List<float> current_predicted_actions = new List<float>();
    float current_action_value = 0f;
    float max_current_action_value = 0f;
    int current_action_index = 0;

    float Q_Value = 0f;

    //Hyperparameters
    public float gamma = 0.01f;
    public float epsilon = 0.1f;
    public float alpha = 0.1f;
    float probability = 0f;

    //Actions
    int acceleration = 0;
    int turning_angle = 0;

    //Reward/Penalties
    float reward = 0f;
    public float speed_reward = 0.0f;
    public float braking_penalty = 0.0f;
    public float collision_penalty = 0.0f;
    public float reverse_penalty = 0.0f;

    public float CalculateReward(List<float> state)
    {/*
        centered = (1 - (distance_from_center / (track_width / 2))
        progression = (progress / 100)
        instability = ((speed / 4) * (abs(steering_angle / 30))

        time = progress / steps
        */
        float reward = 0f;

        //Unpack list into the variables
        (float m_steeringAngle, float velocity_x, float velocity_z, float collision_time, float fl_hit, 
        float fr_hit, float l_hit, float r_hit, float rl_hit, float rr_hit, float acceleration) = (state[0], state[1], state[2], state[3], state[4], state[5], state[6], state[7], state[8], state[9], state[10]);
       
        //Calculate reward
        

        return reward; 
    }

    public float CalculateQ(float q_p, float m_q_c, float r)
    {
        //Q(s, A) = Q(s, A) + alpha[reward + gamma*max_A(Q(s, A') - Q(s, A)]
        //Value of previous s/a = value of previous s/a + lr * reward for current s/a + df * current s/a - previous s/a
        float q_value = q_p + alpha * (r + gamma * m_q_c - q_p);

        return q_value;
    }

    public void TakeAction()
    {
        if (!AI_Controlled)
        {
            m_horizontalInput = Input.GetAxis("Horizontal");
            m_verticalInput = Input.GetAxis("Vertical");
        }
        else
        {
            m_horizontalInput = acceleration;
            m_verticalInput = turning_angle;
        }
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        previous_state = current_state;
        previous_predicted_actions = current_predicted_actions;
        previous_action_value = current_action_value;
        previous_action_index = current_action_index;

        //Get current state after taking actions
        current_state = agent.GetState();

        //Get reward for taking those actions 
        reward = CalculateReward(current_state);

        //Calculate action for current state
        current_predicted_actions = model.Predict(current_state);
        max_current_action_value = current_predicted_actions.Max();

        //Get probability of taking a random action
        probability = (float)(random.NextDouble());

        //Take random action
        if (probability <= epsilon)
            current_action_index = random.Next(0, 9);
        else
            current_action_index = current_predicted_actions.IndexOf(max_current_action_value);

        current_action_value = current_predicted_actions[current_action_index];

        //Take action 
        acceleration = current_action_index / 3 - 1;
        turning_angle = current_action_index % 3 - 1;

        TakeAction(acceleration, turning_angle);

        //Get Q Value for state/action pair
        Q_Value = CalculateQ(previous_action_value, max_current_action_value, reward);

        previous_predicted_actions[previous_action_index] = Q_Value;

        //Call Model.Fit with x as state and y as calculated q value
        model.Fit(previous_state, previous_predicted_actions);
    }
   
}