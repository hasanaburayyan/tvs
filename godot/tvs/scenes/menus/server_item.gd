class_name ServerItem
extends HBoxContainer

@onready var creator: Label = %Creator
@onready var player_count: Label = %PlayerCount
@onready var session_id: Label = %SessionID
@onready var enlist_button: Button = %Enlist
@onready var state: Label = %State

var hud: HUD

func _ready() -> void:
	enlist_button.pressed.connect(func():
		SpacetimeDB.Tvs.reducers.join_game(int(self.session_id.text))
		hud.close_menus()
		hud.start_lobby.emit(int(self.session_id.text))
	)

func _process(delta: float) -> void:
	pass

func populate(creator: String, current_players: int, max_players: int, session_id: int, state: String):
	if current_players >= max_players:
		self.enlist_button.disabled = true
	self.creator.text = creator
	self.player_count.text = "%d/%d" % [current_players, max_players]
	self.session_id.text = str(session_id)
	self.state.text = state
	
