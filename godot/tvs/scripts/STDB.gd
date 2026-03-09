class_name STDB
extends Node

var db: TvsModuleDb:
	get:
		return SpacetimeDB.Tvs.db
		

var local_identity: PackedByteArray = []
