class_name HUD
extends CanvasLayer

signal start_lobby(id: int)

enum MENUS {
	UNKNOWN = 0,
	PLAYER_CREATION = 1,
	LOBBY = 2,
}

@onready var player_creation: PopulableMenu = %PlayerCreation
@onready var lobby_scene: PopulableMenu = %LobbyScene

var menus: Dictionary[MENUS, PopulableMenu] = {}

func _ready():
	menus = {
		MENUS.PLAYER_CREATION: player_creation,
		MENUS.LOBBY: lobby_scene
	}

func close_menus():
	for menu in menus.values():
		menu.visible = false
		

func switch_to_menu(menu: MENUS):
	close_menus()
	menus[menu].visible = true
	menus[menu].populate()
	
