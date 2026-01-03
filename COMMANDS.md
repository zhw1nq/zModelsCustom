# zModelsCustom - Commands

## Player Commands

| Command                     | Description               |
| --------------------------- | ------------------------- |
| `!svip` `!vip` `!md` `!mds` | Mở website models         |
| `!reloadmodels` `!rlmodels` | Reload config (@css/root) |

---

## Web API Commands (Console Only)

### css_webquery
Apply model/weapon/smoke/trail/tracer cho player.

```bash
# Model (slot: CT, T, ALL)
css_webquery model <steamid> <uniqueid> <slot>
css_webquery model 76561198xxx agent_sas CT
css_webquery model 76561198xxx agent_sas T
css_webquery model 76561198xxx agent_sas ALL

# Weapon
css_webquery weapon <steamid> <uniqueid>
css_webquery weapon 76561198xxx ak47_neon

# Smoke color
css_webquery smoke <steamid> <color>
css_webquery smoke 76561198xxx "255 0 0"
css_webquery smoke 76561198xxx random

# Trail
css_webquery trail <steamid> <uniqueid>
css_webquery trail 76561198xxx energycircltrail

# Tracer
css_webquery tracer <steamid> <uniqueid>
css_webquery tracer 76561198xxx energycirctracer
```

---

### css_webdelete
Xóa model/weapon/smoke/trail/tracer của player.

```bash
css_webdelete model <steamid>
css_webdelete weapon <steamid>
css_webdelete smoke <steamid>
css_webdelete trail <steamid>
css_webdelete tracer <steamid>
```

---

### css_webreload
Reload config và refresh player cụ thể.

```bash
css_webreload <steamid>
```

---

### css_weblogin
Hiển thị thông báo login cho player.

```bash
css_weblogin <steamid> <token>
```

---

## Config Files

| File            | Description         |
| --------------- | ------------------- |
| `zConfig.json`  | Database + settings |
| `zModels.json`  | Player models       |
| `zWeapons.json` | Weapon skins        |
| `zTrails.json`  | Trail effects       |
| `zTracers.json` | Tracer effects      |

---

## Database Tables

| Table           | Columns                 | Description    |
| --------------- | ----------------------- | -------------- |
| `zPlayerModels` | steamid, uniqueid, slot | Player models  |
| `zWeapons`      | steamid, uniqueid       | Weapon skins   |
| `zSmokeColors`  | steamid, color          | Smoke colors   |
| `zTrails`       | steamid, uniqueid       | Trail effects  |
| `zTracers`      | steamid, uniqueid       | Tracer effects |
