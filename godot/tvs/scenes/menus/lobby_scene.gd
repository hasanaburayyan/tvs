extends PopulableMenu

@export var hud: HUD

@onready var server_list_container: VBoxContainer = %ServerListContainer
@onready var button_container: HBoxContainer = %ButtonContainer
@onready var refresh_button: Button = %RefreshButton

var server_item_scene: PackedScene = preload("uid://b7ct4i3mjdb0l")

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	refresh_button.pressed.connect(_on_refresh_pressed)

func _on_join_pressed():
	pass

func _on_refresh_pressed():
	populate()

func _clear_children():
	for child in server_list_container.get_children():
		server_list_container.remove_child(child)

func populate():
	print("Populating lobby")
	_clear_children()
	var game_players = SpacetimeDB.Tvs.db.game_player.iter()
	for session in SpacetimeDB.Tvs.db.game_session.iter():
		var server_item = server_item_scene.instantiate() as ServerItem
		server_item.hud = self.hud
		server_list_container.add_child(server_item)
		
		var game_owner = SpacetimeDB.Tvs.db.player.identity.find(session.owner_identity)
		var current_player_count = 0
		for game_player in game_players:
			if game_player.game_session_id == session.id:
				current_player_count += 1
		server_item.populate(game_owner.name, current_player_count, session.max_players, session.id, str(session.state))
