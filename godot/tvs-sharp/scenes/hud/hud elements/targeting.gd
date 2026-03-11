extends Control

var camera: Camera3D
var viewport: Viewport

var current_target: Node3D = null

@onready var top_left: ColorRect = %TopLeft
@onready var top_right: ColorRect = %TopRight
@onready var bottom_left: ColorRect = %BottomLeft
@onready var bottom_right: ColorRect = %BottomRight

var corner_offset = Vector2(15,15)
var ray_length: float = 1000 #This shit could be acquisition range by weapon, or when using binoculars or some shit.

func _ready() -> void:
	viewport = get_viewport()
	camera = get_viewport().get_camera_3d()

func _input(event):
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
			if camera == null or !is_instance_valid(camera) or !camera.is_inside_tree():
				camera = get_viewport().get_camera_3d()
			if camera == null:
				return
			var mouse_pos = event.position
			var ray_origin = camera.project_ray_origin(mouse_pos)
			var ray_end = ray_origin + camera.project_ray_normal(mouse_pos) * ray_length
			var query = PhysicsRayQueryParameters3D.create(ray_origin, ray_end)
			var result = get_viewport().world_3d.direct_space_state.intersect_ray(query)
			current_target = _get_target_from_raycast(result)

func _process(delta: float) -> void:
	if current_target != null and (!is_instance_valid(current_target) or !current_target.is_inside_tree()):
		current_target = null
	if current_target == null:
		hide()
		return
	if camera == null or !is_instance_valid(camera):
		camera = get_viewport().get_camera_3d()
	if camera == null:
		hide()
		return
	show()
	_update_corner_positions()

func _update_corner_positions():
	if current_target == null or !is_instance_valid(current_target) or !current_target.is_inside_tree():
		current_target = null
		hide()
		return
	if camera != null and camera.is_position_behind(current_target.global_position):
		hide()
		return

	var aabb = _get_target_aabb(current_target)
	var screen_rect = _aabb_to_screen_rect(aabb)

	top_left.position = screen_rect.position + Vector2(-corner_offset.x, -corner_offset.y)
	top_right.position = Vector2(screen_rect.end.x, screen_rect.position.y) + Vector2(corner_offset.x, -corner_offset.y)
	bottom_left.position = Vector2(screen_rect.position.x, screen_rect.end.y) + Vector2(-corner_offset.x, corner_offset.y)
	bottom_right.position = screen_rect.end + Vector2(corner_offset.x, corner_offset.y)
	
func _get_target_from_raycast(result: Dictionary) -> Node3D:
	if result.is_empty():
		return null
	
	var collider = result.collider
	if collider and _has_collision_shape(collider):
		return collider as Node3D
	return null

func _has_collision_shape(node: Node) -> bool:
	var shapes = node.find_children("*", "CollisionShape3D", true, false)
	return shapes.size() > 0

func _get_target_aabb(target: Node3D) -> AABB:
	var shapes = target.find_children("*", "CollisionShape3D", true, false)
	if shapes.size() == 0:
		return AABB(target.global_position - Vector3.ONE * 0.5, Vector3.ONE)
	
	var first_shape: CollisionShape3D = shapes[0]
	var aabb = first_shape.global_transform * _shape_to_local_aabb(first_shape.shape)
	for i in range(1, shapes.size()):
		var shape: CollisionShape3D = shapes[i]
		aabb = aabb.merge(shape.global_transform * _shape_to_local_aabb(shape.shape))
	return aabb

func _shape_to_local_aabb(shape: Shape3D) -> AABB:
	if shape is BoxShape3D:
		return AABB(-shape.size / 2, shape.size)
	elif shape is CapsuleShape3D:
		var r = shape.radius
		var h = shape.height / 2
		return AABB(Vector3(-r, -h, -r), Vector3(r * 2, shape.height, r * 2))
	elif shape is SphereShape3D:
		var r = shape.radius
		return AABB(Vector3(-r, -r, -r), Vector3(r * 2, r * 2, r * 2))
	elif shape is CylinderShape3D:
		var r = shape.radius
		var h = shape.height / 2
		return AABB(Vector3(-r, -h, -r), Vector3(r * 2, shape.height, r * 2))
	else:
		return AABB(Vector3(-0.5, -0.5, -0.5), Vector3.ONE)

func _aabb_to_screen_rect(aabb: AABB) -> Rect2:
	var min_screen = Vector2(INF, INF)
	var max_screen = Vector2(-INF, -INF)

	for i in range(8):
		var corner = aabb.get_endpoint(i)
		var screen_point = camera.unproject_position(corner)
		min_screen = min_screen.min(screen_point)
		max_screen = max_screen.max(screen_point)

	return Rect2(min_screen, max_screen - min_screen)
