"""In-place boot contacts and two-handed carry for the field uniform's Generic rig."""
from math import cos, pi, radians, sin, sqrt
from mathutils import Euler, Vector


# Twelve evenly spaced samples from first heel contact to the following contact.
# Curves give ankle travel (front is negative Y), height over the ground and boot pitch.
WALK = (
    (-.255, -.196, -.115, -.015, .085, .185, .248, .220, .108, -.061, -.205, -.263),
    (.159, .150, .149, .149, .150, .162, .192, .222, .229, .212, .189, .173),
    (-12, -2, 0, 0, 2, 7, 18, 23, 12, -2, -9, -15),
)
RUN = (
    (-.276, -.175, -.035, .118, .256, .345, .322, .205, .020, -.162, -.265, -.302),
    (.166, .152, .147, .151, .183, .245, .346, .404, .371, .286, .211, .175),
    (-7, -2, 1, 7, 21, 32, 38, 24, 7, -6, -12, -12),
)


def cyclic(values, phase):
    sample = (phase % 1) * len(values)
    index = int(sample)
    t = sample - index
    a, b, c, d = (values[(index+i) % len(values)] for i in (-1, 0, 1, 2))
    return b + .5*t*(c-a + t*(2*a - 5*b + 4*c-d + t*(3*(b-c)+d-a)))


def rotation(x=0, y=0, z=0):
    return Euler((radians(x), radians(y), radians(z)), "XYZ").to_quaternion()


def stride(arm, phase, running, role):
    bones = arm.pose.bones
    posed = {}

    def base(name):
        bone = bones[name].bone
        if bone.parent is None:
            return bone.matrix_local.copy()
        return posed[bone.parent.name] @ bone.parent.matrix_local.inverted() @ bone.matrix_local

    def turn(name, target):
        reference = base(name)
        bone = bones[name]
        bone.rotation_euler = (reference.to_quaternion().inverted() @ target).to_euler("XYZ")
        posed[name] = reference @ bone.matrix_basis

    def delta(name, orient):
        turn(name, orient @ base(name).to_quaternion())

    def aim(name, target):
        reference = base(name)
        previous = reference.to_quaternion()
        direction = previous @ Vector((0, 1, 0))
        turn(name, direction.rotation_difference((target-reference.translation).normalized()) @ previous)

    def two_bones(upper, lower, end, target, pole, end_frame):
        joint = base(upper).translation
        line = target-joint
        axis = line.normalized()
        length_a, length_b = bones[upper].bone.length, bones[lower].bone.length
        distance = max(abs(length_a-length_b)+.0005, min(line.length, length_a+length_b-.0005))
        along = (length_a**2 + distance**2-length_b**2)/(2*distance)
        bend = (pole-joint) - axis*(pole-joint).dot(axis)
        hinge = joint + axis*along + bend.normalized()*sqrt(max(.00001, length_a**2-along**2))
        aim(upper, hinge)
        aim(lower, joint+axis*distance)
        turn(end, end_frame @ bones[end].bone.matrix_local.to_quaternion())

    wave = 2*pi*phase
    side = sin(wave)
    down = cos(2*wave-1.5)
    shoulder = cos(wave)
    # The foot that accepts the weight pulls the hips toward its side.
    hip_shift = Vector((-(.027 if running else .041)*side, -.014 if running else 0,
                        (-.103 if running else -.063) - (.027 if running else .018)*down))
    pelvis = bones["Pelvis"]
    pelvis.location = pelvis.bone.matrix_local.to_quaternion().inverted() @ hip_shift
    delta("Pelvis", rotation(3.5 if running else 1.5, (3 if running else 3.8)*side, 4.5*shoulder))
    delta("Chest", rotation((8 if running else 3)+1.4*down, -2.2*side, -8*shoulder))
    delta("Neck", rotation(-2 if running else -1, -.9*side, 2*shoulder))
    delta("Head", rotation(-2.5 if running else -1, .6*side, 1.2*shoulder))

    travels, heights, pitches = RUN if running else WALK
    for sign, prefix, offset in ((-1,"L",0), (1,"R",.5)):
        p = (phase+offset) % 1
        lift = max(0, -sin(2*pi*p))
        foot = Vector((sign*(.117 + .014*lift), cyclic(travels,p), cyclic(heights,p)))
        # The pole stays ahead of the thigh and just outside the centre line.
        pole = Vector((sign*.155,-.9,.48))
        two_bones(prefix+"Thigh",prefix+"Shin",prefix+"Foot",foot,pole,rotation(cyclic(pitches,p)))

    chest_delta = posed["Chest"] @ bones["Chest"].bone.matrix_local.inverted()
    carry = rotation(2.3*down if running else .9*down, 1.15*side, -2*shoulder)
    anchor = bones["RHand"].bone.head_local.copy()
    # Keep both wrists on the same rigid object. The arms absorb the movement of the shoulders.
    lag = Vector((-.012*side, (.047 if running else .030)*down,
                  (.043 if running else .022)*sin(2*wave-.75)))
    if role == 0:
        lag *= .84
    for sign, prefix in ((-1,"L"),(1,"R")):
        wrist = bones[prefix+"Hand"].bone.head_local
        target = chest_delta @ (anchor + carry @ (wrist-anchor) + lag)
        pole = chest_delta @ (bones[prefix+"Forearm"].bone.head_local +
                              Vector((sign*(.025+.012*side),.023,-.018)))
        two_bones(prefix+"Arm",prefix+"Forearm",prefix+"Hand",target,pole,chest_delta.to_quaternion() @ carry)
