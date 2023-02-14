import numpy as np
import tensorflow as tf
import socket
import sys

INPUT_LENGTH = 12

def create_model(input_shape: tuple):
    # Expected reward: no turn, turn left, turn right, neutral, break, accelerate
    y = x = tf.keras.Input(input_shape)
    y = tf.keras.layers.Dense(128, activation="swish")(y)
    y = tf.keras.layers.Dense(128, activation="swish")(y)
    y = tf.keras.layers.Dense(64, activation="swish")(y)
    y = tf.keras.layers.Dense(64, activation="swish")(y)
    y = tf.keras.layers.Dense(6)(y)
    y = tf.keras.layers.Reshape((6,))(y)
    model = tf.keras.Model(x,y)
    model.compile(
        loss=tf.keras.losses.MeanSquaredError(),
        optimizer=tf.keras.optimizers.Adam(1e-4)
    )
    model.summary()

    return model

def create_server():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(('', 5004))
    s.settimeout(1.0)
    return s


def parse_data(data):
    # [state (16)][chosen action (1)][reward (4)]
    values = np.expand_dims(np.frombuffer(data, dtype=np.float32, count=INPUT_LENGTH), 0)
    if len(data) > 4*INPUT_LENGTH:
        chosen_actions = np.frombuffer(data, dtype=np.int32, offset=4*INPUT_LENGTH, count=2)
        reward = np.frombuffer(data, dtype=np.float32, offset=4*(INPUT_LENGTH + 2))[0]
        return values, chosen_actions, reward
    return values, None, None 


def main(argv):
    print("Runnning...")
    server = create_server()

    model = create_model((INPUT_LENGTH,))

    is_running = True
    while is_running:
        try:
            data, addr = server.recvfrom(1024)
        except TimeoutError as e: 
            continue

        try:
            input_values, chosen_actions, reward = parse_data(data)
        except ValueError as e:
            print("Malformed packet", e)
            print(data)
            continue
        if chosen_actions is not None:
            expected_rewards = model.predict(input_values, verbose=0)
            expected_rewards[0][chosen_actions[0]] = reward
            expected_rewards[0][chosen_actions[1]] = reward
            print("Chosen actions:", chosen_actions)
            print("Actual Reward:", reward)
            model.fit(input_values, expected_rewards, verbose=0)
        else:
            expected_rewards = model.predict(input_values, verbose=0).squeeze()
            server.sendto(expected_rewards.tobytes(),addr)
        print("Expected rewards:", expected_rewards)
        print()

    server.close()

if __name__ == "__main__":
    main(sys.argv)


# 1. Look at our current state (velocity, orientation, etc.)
# 2. Predict the actions to take at current state
# 3. Perform the actions to enter the next state
# 4. Compute the reward of what happened changing states
# 5. Fit the model at the previous state with the observed reward
