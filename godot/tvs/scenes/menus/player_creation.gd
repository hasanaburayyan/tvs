extends PopulableMenu

@export var hud: HUD
@onready var username_input: LineEdit = %UsernameInput
@onready var create_player_button: Button = %CreatePlayerButton

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	create_player_button.pressed.connect(func():
		SpacetimeDB.Tvs.reducers.create_player(username_input.text)
		self.visible = false
		hud.switch_to_menu(HUD.MENUS.LOBBY)
	)
