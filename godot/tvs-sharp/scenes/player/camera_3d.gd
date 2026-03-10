class_name FreelookCamera
extends Camera3D

@export_range(0.0, 1.0) var sensitivity = 0.25

var _mouse_position = Vector2(0.0, 0.0)
var _total_pitch = 0.0

var _direction = Vector3(0.0, 0.0, 0.0)
var _velocity = Vector3(0.0, 0.0, 0.0)
var _acceleration = 30
var _deceleration = -10
var _vel_multiplier = 4

var _w = false
var _s = false
var _a = false
var _d = false
var _q = false
var _e = false

func _input(event):
	if event is InputEventMouseMotion:
		_mouse_position = event.relative
	
	if event is InputEventMouseButton:
		match event.button_index:
			MOUSE_BUTTON_RIGHT:
				Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED if event.pressed else Input.MOUSE_MODE_VISIBLE)
			MOUSE_BUTTON_WHEEL_UP:
				_vel_multiplier = clamp(_vel_multiplier / 1.1, 0.2, 20)
			MOUSE_BUTTON_WHEEL_DOWN:
				_vel_multiplier = clamp(_vel_multiplier / 1.1, 0.2, 20)
	
	if event is InputEventKey:
		match event.keycode:
			KEY_R:
				if event.pressed and not event.echo:
					if Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:
						Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
					else:
						Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
			KEY_S:
				_s = event.pressed
			KEY_W:
				_w = event.pressed
			KEY_A:
				_a = event.pressed
			KEY_D:
				_d = event.pressed
			KEY_Q:
				_q = event.pressed
			KEY_E:
				_e = event.pressed

func _process(delta: float) -> void:
	#_update_movement(delta)
	_update_mouselook()

func _update_mouselook():
	if Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:
		_mouse_position *= sensitivity
		var yaw = _mouse_position.x
		var pitch = _mouse_position.y
		_mouse_position = Vector2(0,0)
		pitch = clamp(pitch, -90 - _total_pitch, 90 - _total_pitch)
		_total_pitch += pitch
		
		get_parent().get_parent().rotate_y(deg_to_rad(-yaw))
		rotate_object_local(Vector3(1,0,0), deg_to_rad(-pitch))
		
#func _update_movement(delta):
	#_direction = Vector3(
	#(_d as float) - (_a as float),
	#(_e as float) - (_q as float),
	#(_s as float) - (_w as float))
	#
	#var offset = _direction.normalized() * _acceleration * _vel_multiplier * delta + _velocity.normalized() * _deceleration * _vel_multiplier * delta
	#
	#if _direction == Vector3.ZERO and offset.length_squared() > _velocity.length_squared():
		#_velocity = Vector3.ZERO
	#else:
		#_velocity.x = clamp(_velocity.x + offset.x, -_vel_multiplier, _vel_multiplier)
		#_velocity.y = clamp(_velocity.y + offset.y, -_vel_multiplier, _vel_multiplier)
		#_velocity.z = clamp(_velocity.z + offset.z, -_vel_multiplier, _vel_multiplier)
		#
		#translate(_velocity * delta)
