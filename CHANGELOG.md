## v1.0.3

- Added a Unity 6 Windows-to-Linux IL2CPP compatibility provider so BuildProgram can find Linux x64 sysroot/toolchain packages.
- Hardened Linux IL2CPP preflight to fail early when Unity cannot see BuildProgram-compatible Linux x64 providers.

## v1.0.2

- Removed Easy Itch Push Player Settings overrides during builds.
- Unity Build Profiles now remain the source of truth for platform settings such as scripting backend, managed stripping, and Strip Engine Code.
- Removed the Release Obfuscation, Force IL2CPP, and Managed Stripping plugin settings.

## v1.0.0

- Added light interruptions after hull impacts. / Р”РѕР±Р°РІРёР»Рё РїРµСЂРµР±РѕРё СЃРІРµС‚Р° РїРѕСЃР»Рµ СѓРґР°СЂРѕРІ РѕР± РєРѕСЂРїСѓСЃ.
- Added credits in the main menu. / Р”РѕР±Р°РІРёР»Рё С‚РёС‚СЂС‹ РІ РіР»Р°РІРЅРѕРµ РјРµРЅСЋ.
- Updated the emitter so it no longer breaks. / РћР±РЅРѕРІРёР»Рё СЌРјРёС‚С‚РµСЂ, С‡С‚РѕР±С‹ РѕРЅ Р±РѕР»СЊС€Рµ РЅРµ Р»РѕРјР°Р»СЃСЏ.
- Added mobile hints for the emitter interaction. / Р”РѕР±Р°РІРёР»Рё РїРѕРґСЃРєР°Р·РєРё РґР»СЏ РІР·Р°РёРјРѕРґРµР№СЃС‚РІРёСЏ СЃ СЌРјРёС‚С‚РµСЂРѕРј РЅР° РјРѕР±РёР»СЊРЅС‹С… СѓСЃС‚СЂРѕР№СЃС‚РІР°С….
- Fixed Android fullscreen without a top safe-area bar. / РСЃРїСЂР°РІРёР»Рё РїРѕР»РЅРѕСЌРєСЂР°РЅРЅС‹Р№ СЂРµР¶РёРј РЅР° Android Р±РµР· РІРµСЂС…РЅРµР№ safe-area РїРѕР»РѕСЃС‹.
- Fixed enemy attack timing bugs. / РСЃРїСЂР°РІРёР»Рё РѕС€РёР±РєРё РІ С‚Р°Р№РјРёРЅРіР°С… Р°С‚Р°Рє РІСЂР°РіРѕРІ.
- Updated difficulty progression. / РћР±РЅРѕРІРёР»Рё РїСЂРѕРіСЂРµСЃСЃРёСЋ СЃР»РѕР¶РЅРѕСЃС‚Рё.
- Lowered the game pace. / РўРµРјРї РїСЂРѕС…РѕР¶РґРµРЅРёСЏ Р±РѕР»РµРµ СЂР°Р·РјРµСЂРµРЅРЅС‹Р№.
- Added clearer health warning feedback. / Р”РѕР±Р°РІРёР»Рё Р±РѕР»РµРµ РїРѕРЅСЏС‚РЅСѓСЋ РѕР±СЂР°С‚РЅСѓСЋ СЃРІСЏР·СЊ РїРѕ РїСЂРµРґСѓРїСЂРµР¶РґРµРЅРёСЋ Рѕ Р·РґРѕСЂРѕРІСЊРµ.
- Added game over when every submarine system is broken. / РќРѕРІРѕРµ СѓСЃР»РѕРІРёРµ РїРѕСЂР°Р¶РµРЅРёСЏ.
- Enemies lag behind earlier when surfacing. / Р’СЂР°РіРё СЂР°РЅСЊС€Рµ РѕС‚СЃС‚Р°СЋС‚ РїСЂРё РІСЃРїР»С‹С‚РёРё РЅР° РїРѕРІРµСЂС…РЅРѕСЃС‚СЊ.
- Increased sonar ping volume. / РЈРІРµР»РёС‡РµРЅР° РіСЂРѕРјРєРѕСЃС‚СЊ РёРјРїСѓР»СЊСЃР° СЃРѕРЅР°СЂР°.
- Added edge glow on the depth gauge to emphasize ascent and descent engine activity. / Р”РѕР±Р°РІР»РµРЅРѕ СЃРІРµС‡РµРЅРёРµ РїРѕ РєСЂР°СЏРј С€РєР°Р»С‹ РіР»СѓР±РёРЅС‹, С‡С‚РѕР±С‹ РїРѕРґС‡РµСЂРєРЅСѓС‚СЊ СЂР°Р±РѕС‚Сѓ РґРІРёРіР°С‚РµР»РµР№ РїСЂРё РІСЃРїР»С‹С‚РёРё Рё РїРѕРіСЂСѓР¶РµРЅРёРё.
- Fixed interactable hover feedback so availability visuals update while the cursor stays on the object. / РСЃРїСЂР°РІР»РµРЅР° РїРѕРґСЃРІРµС‚РєР° РёРЅС‚РµСЂР°РєС‚РёРІРЅС‹С… РѕР±СЉРµРєС‚РѕРІ: РІРёР·СѓР°Р»СЊРЅР°СЏ РґРѕСЃС‚СѓРїРЅРѕСЃС‚СЊ С‚РµРїРµСЂСЊ РѕР±РЅРѕРІР»СЏРµС‚СЃСЏ, РїРѕРєР° РєСѓСЂСЃРѕСЂ РѕСЃС‚Р°РµС‚СЃСЏ РЅР° РѕР±СЉРµРєС‚Рµ.
- Added a dithering effect. / Р”РѕР±Р°РІР»РµРЅ СЌС„С„РµРєС‚ РґРёР·РµСЂРёРЅРіР°.
- Fixed the CRT effect toggle so lens distortion is restored when CRT is turned back on. / РСЃРїСЂР°РІР»РµРЅРѕ РїРµСЂРµРєР»СЋС‡РµРЅРёРµ CRT-СЌС„С„РµРєС‚Р°: РёСЃРєР°Р¶РµРЅРёРµ Р»РёРЅР·С‹ СЃРЅРѕРІР° РІРѕСЃСЃС‚Р°РЅР°РІР»РёРІР°РµС‚СЃСЏ РїСЂРё РїРѕРІС‚РѕСЂРЅРѕРј РІРєР»СЋС‡РµРЅРёРё CRT.
- Linked chromatic aberration to the CRT effect toggle so it turns off together with the CRT scan effect. / РҐСЂРѕРјР°С‚РёС‡РµСЃРєР°СЏ Р°Р±РµСЂСЂР°С†РёСЏ РїСЂРёРІСЏР·Р°РЅР° Рє РїРµСЂРµРєР»СЋС‡Р°С‚РµР»СЋ CRT-СЌС„С„РµРєС‚Р° Рё С‚РµРїРµСЂСЊ РѕС‚РєР»СЋС‡Р°РµС‚СЃСЏ РІРјРµСЃС‚Рµ СЃРѕ СЃРєР°РЅРёСЂСѓСЋС‰РёРј CRT-СЌС„С„РµРєС‚РѕРј.
- Updated CRT effect defaults so lens distortion and chromatic aberration keep their configured enabled intensities after scene and volume-profile refreshes. / РћР±РЅРѕРІР»РµРЅС‹ Р·РЅР°С‡РµРЅРёСЏ CRT-СЌС„С„РµРєС‚Р° РїРѕ СѓРјРѕР»С‡Р°РЅРёСЋ: РёСЃРєР°Р¶РµРЅРёРµ Р»РёРЅР·С‹ Рё С…СЂРѕРјР°С‚РёС‡РµСЃРєР°СЏ Р°Р±РµСЂСЂР°С†РёСЏ СЃРѕС…СЂР°РЅСЏСЋС‚ РЅР°СЃС‚СЂРѕРµРЅРЅСѓСЋ РёРЅС‚РµРЅСЃРёРІРЅРѕСЃС‚СЊ РїРѕСЃР»Рµ РѕР±РЅРѕРІР»РµРЅРёСЏ СЃС†РµРЅС‹ Рё РїСЂРѕС„РёР»СЏ РѕР±СЉРµРјР°.
- Reduced Acoustic Visualizer texture size for better readability. / РЈРјРµРЅСЊС€РµРЅ СЂР°Р·РјРµСЂ С‚РµРєСЃС‚СѓСЂС‹ Р’РёР·СѓР°Р»РёР·Р°С‚РѕСЂР° Р°РєСѓСЃС‚РёРєРё РґР»СЏ Р»СѓС‡С€РµР№ С‡РёС‚Р°РµРјРѕСЃС‚Рё.
- Added interactive note inspection with cursor movement and zoom. / Р”РѕР±Р°РІР»РµРЅ РёРЅС‚РµСЂР°РєС‚РёРІРЅС‹Р№ РїСЂРѕСЃРјРѕС‚СЂ Р·Р°РїРёСЃРєРё СЃ РїРµСЂРµРјРµС‰РµРЅРёРµРј РїРѕ РєСѓСЂСЃРѕСЂСѓ Рё Р·СѓРјРѕРј.
