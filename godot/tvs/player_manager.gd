class_name PlayerManager
extends Node

var lobby_id = -1
var player_scene: PackedScene = preload("uid://cvw20tgjayvv5")

@export var player_spawn_path: Node

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	pass
	
func set_lobby(id: int):
	lobby_id = id

func load_lobby():
	print("loading lobby: %d" % lobby_id)
	for game_player in SpacetimeDB.Tvs.db.game_player.iter():
		if game_player.game_session_id == lobby_id:
			_spawn_player(game_player)

func _spawn_player(game_player: TvsGamePlayer):
	var player_obj = player_scene.instantiate() as GamePlayer
	var player = SpacetimeDB.Tvs.db.player.identity.find(game_player.player_identity)
	player_obj.name = player.name
	player_obj.owner_id = player.identity
	player_obj.position = Vector3(game_player.position.x, game_player.position.y, game_player.position.z)
	player_obj.game_id = lobby_id
	player_spawn_path.add_child(player_obj)


func _on_game_player_insert(game_player: TvsGamePlayer):
	if game_player.game_session_id != lobby_id:
		return
	_spawn_player(game_player)
	
func _on_game_player_delete(game_player: TvsGamePlayer):
	if game_player.game_session_id != lobby_id:
		return
	


func _on_game_player_update(old_game_player: TvsGamePlayer, update_game_player: TvsGamePlayer):
	if old_game_player.game_session_id != lobby_id and update_game_player.game_session_id != lobby_id:
		return
		
	for node in player_spawn_path.get_children():
		if node is GamePlayer:
			if node.owner_id == update_game_player.player_identity:
				node.position = Vector3(
					update_game_player.position.x,
					update_game_player.position.y,
					update_game_player.position.z
				)
