# Box storage shelves

Use `Assets/Prefabs/LevelAssets/BoxStorageShelf.prefab`. It has two compartments and is registered in the normal and default network prefab lists.

- Carry a purchased ingredient box with Shift+F.
- Hold E and aim into an empty compartment. The hologram snaps to its fitted position.
- Release E to secure the box. Full compartments, oversized boxes, and obstructed positions are rejected by the host.
- Use F to take/return ingredients as usual, or Shift+F to remove the box. Removing it frees the compartment.

Place the shelf prefab in the networked gameplay scene for stationary storage. For truck storage, add it to `VehicleKitchen > Stations` with a mount marker; it will spawn separately and follow the truck. Do not nest its NetworkObject inside the truck prefab. The supplied shelf is 1.7 units wide and 2.7 high; fit the furniture and compartments to the available interior before mounting it.

Each child BoxCollider trigger in **Compartments** defines one slot's usable space and orientation. Its lower face is the box's resting height, and its local X/Z center controls alignment. Keep that lower face at the top of the physical shelf board. The box's actual bounds determine its offset without resizing it. Keep shelf and compartment scales positive; unit scale is simplest. Fixed shelves need no Rigidbody.

Occupancy uses the existing replicated item attachment state. Attached boxes are kinematic and preserve their stock. Host validation repeats the same placement checks, so simultaneous requests cannot both fill a compartment. Boxes and their attachment states resolve for late joiners through the existing item system.

Runtime compilation passed. Verify placing/removing boxes, simultaneous placement, late joining, and truck-mounted use in multiplayer Play mode.
