class_name GamePlayer
extends CharacterBody3D

@onready var camera_3d: Camera3D = $Camera3D

const SPEED = 5.0
const JUMP_VELOCITY = 4.5
const SYNC_INTERVAL := 0.05 # 20 updates/sec to SpacetimeDB

var owner_id: PackedByteArray = []
var game_id: int
var is_local: bool = false

var _sync_timer: float = 0.0
var _last_synced_position := Vector3.ZERO

func _ready() -> void:
	is_local = owner_id == Stdb.local_identity
	if is_local:
		camera_3d.make_current()

func _physics_process(delta: float) -> void:
	if not is_local:
		return

	if not is_on_floor():
		velocity += get_gravity() * delta

	if Input.is_action_just_pressed("ui_accept") and is_on_floor():
		velocity.y = JUMP_VELOCITY

	var input_dir := Input.get_vector("ui_left", "ui_right", "ui_up", "ui_down")
	var direction := (transform.basis * Vector3(input_dir.x, 0, input_dir.y)).normalized()
	if direction:
		velocity.x = direction.x * SPEED
		velocity.z = direction.z * SPEED
	else:
		velocity.x = move_toward(velocity.x, 0, SPEED)
		velocity.z = move_toward(velocity.z, 0, SPEED)

	move_and_slide()

	_sync_timer += delta
	if _sync_timer >= SYNC_INTERVAL and position.distance_squared_to(_last_synced_position) > 0.001:
		_last_synced_position = position
		_sync_timer = 0.0
		SpacetimeDB.Tvs.reducers.move_player(
			game_id,
			TvsDbVector3.create(position.x, position.y, position.z)
		)
