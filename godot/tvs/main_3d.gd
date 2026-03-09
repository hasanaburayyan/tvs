extends Node3D

@onready var player_manager: PlayerManager = $PlayerManager
@onready var hud: HUD = $Hud


# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	SpacetimeDB.Tvs.connected.connect(_on_spacetime_connected)
	SpacetimeDB.Tvs.disconnected.connect(_on_spacetimedb_disconnected)
	SpacetimeDB.Tvs.connection_error.connect(_on_spacetimedb_connection_error)
	SpacetimeDB.Tvs.transaction_update_received.connect(_on_transaction_update_recieved)
	
	SpacetimeDB.Tvs.connect_db(
		"http://127.0.0.1:3000",
		"tvs"
	)
	
	hud.start_lobby.connect(func(id):
		player_manager.lobby_id = id
		player_manager.load_lobby()
	)
	


func _on_transaction_update_recieved(transaction_message: TransactionUpdateMessage):
	if transaction_message.status.status_type == UpdateStatusData.StatusType.FAILED:
		printerr("Reducer call (ReqID: %d) failed: %s" % [transaction_message.reducer_call.request_id, transaction_message.status.failure_message])
	elif transaction_message.status.status_type == UpdateStatusData.StatusType.COMMITTED:
		print("Reducer call (ReqID: %d) committed." % transaction_message.reducer_call.request_id)


func _on_spacetime_connected(identity: PackedByteArray, token: String):
	print("Game Connected!!")
	Stdb.local_identity = identity
	var sub_queries = [
		"SELECT * FROM player",
		"SELECT * FROM game_session",
		"SELECT * FROM game_player"
	]
	
	var subscription = SpacetimeDB.Tvs.subscribe(sub_queries)
	if subscription.error:
		printerr("Subscription failed")
	
	subscription.applied.connect(func():
		SpacetimeDB.Tvs.db.game_player.on_update(player_manager._on_game_player_update)
		SpacetimeDB.Tvs.db.game_player.on_delete(player_manager._on_game_player_delete)
		SpacetimeDB.Tvs.db.game_player.on_insert(player_manager._on_game_player_insert)
	)
	


func _on_spacetimedb_disconnected():
	print("client disconnected")
	
func _on_spacetimedb_connection_error(code: int, reason: String):
	print("Could not connect %s" % reason)

# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass
