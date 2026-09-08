# Turkish (Türkçe) Terminology & Style Glossary — RimWorld Access

This glossary locks in consistent Turkish wording for translating the RimWorld Access screen-reader
mod. The goal is that Turkish players feel the mod is a seamless extension of RimWorld itself, so
**every game-anchored term below uses the EXACT word RimWorld's own official Turkish translation
uses**, with a citation to the reference file it was taken from.

The strings we translate are **spoken aloud by a TTS engine**, not displayed. Natural, terse,
unambiguous phrasing matters more than visual polish.

Reference corpus: RimWorld's official Turkish, extracted from `Core/Languages/Turkish (Türkçe).tar`
plus the Royalty, Ideology, Biotech, Anomaly and Odyssey equivalents. Paths below are relative to the
extracted corpus root (`Core/`, `Royalty/`, `Ideology/`, `Biotech/`, `Anomaly/`, `Odyssey/`).

> How to use this file: grep for the English term. If a term you need is not here and RimWorld has
> it, grep the game corpus yourself (see Section 4) and add it — never invent a rendering for a
> concept the game already names.

---

## Section 0 — Language folder name (READ FIRST)

**Ship the mod's Turkish strings in `Languages/Turkish/` — the pure-ASCII legacy name, NOT
`Turkish (Türkçe)`.**

Core ships the language as `Turkish (Türkçe).tar`, but a mod adding to an existing language is merged
by `LanguageDatabase.InitLanguageMetadataFrom`, which matches on
`folderName == langDir.Name || LegacyFolderName == langDir.Name`. `LegacyFolderName` is the part
before the `(` — i.e. `Turkish`. Naming our folder with the native form would embed `ü` (U+00FC), a
non-ASCII character whose byte representation changes under Unicode normalization (NFC vs NFD). Zip
the mod on macOS, unzip on Windows, and the name can flip to a decomposed form that no longer
byte-matches Core's folder. The game then registers our folder as a **separate** `LoadedLanguage` and
the player sees **two** identical-looking Turkish entries in the language menu.

`Turkish` has no non-ASCII bytes, so it always matches Core's `LegacyFolderName` and merges
correctly on every platform. This matches every language the mod already ships (`Languages/Ukrainian/`,
`Languages/ChineseSimplified/`, `Languages/SpanishLatin/`). The menu still displays «Türkçe» — that
comes from Core's entry, which our folder merges into. No `LanguageInfo.xml` is needed.

Mirror `Languages/English/Keyed/` exactly: we ship **only** `Keyed/`.

---

## Section 1 — Core game vocabulary (game-anchored)

Every row is the term RimWorld itself ships in Turkish. Use it verbatim. These are the words the
player already hears from the game, so our mod must match them exactly.

### People, factions, world

| English | Türkçe | Source file |
|---|---|---|
| colonist | kolonici | `Core/Keyed/Misc.xml` (`Colonist` kolonici) |
| colony | koloni | `Core/Keyed/Designators.xml` (`DesignatorClaimDesc` …koloni için sahiplen) |
| pawn / character (generic) | kişi | `Biotech/Keyed/ITabs.xml` (`ViewGenesDesc` {PAWN_nameDef} adlı **kişinin** genleri). Also **karakter** for the combat sense: `Core/Keyed/GameplayCommands.xml` (`CommandFireAtWillDesc` …bu **karakter**…) |
| faction | fraksiyon | `Core/Keyed/Misc_Gameplay.xml` (`Faction` Fraksiyon) |
| caravan | kervan | `Core/Keyed/Misc_Gameplay.xml` (`Caravan` Kervan) |
| raid | baskın | `Core/Keyed/Misc_Gameplay.xml` (`Raid` Baskın) |
| map | harita | `Core/Keyed/Misc_Gameplay.xml` (`Map` Harita) |
| map cell / square | kare | `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` Aynı **karedeki** bir sonraki şeyi seç) |
| world tile (hex) | altıgen | `Biotech/Keyed/Dialogs_Various.xml` (`ConfirmSettleNearPollution` Bu **altıgen**, kirli **altıgenlerin** yakınında) |
| area | alan | `Core/Keyed/Misc_Gameplay.xml` (`AreaLower` alan) |
| zone | bölge | `Core/Keyed/Misc_Gameplay.xml` (`Zone` Bölge); `Core/DefInjected/DesignationCategoryDef/*` (`Zone.label` bölge) |
| world / planet | dünya / gezegen | `Core/Keyed/Dialogs_Various.xml` (`SearchTheWorld` **Dünyayı** ara); `Core/Keyed/Menu_Options.xml` (`ZoomSwitchLayer_Tooltip` …yeniden **gezegeni** gösterir) |
| room | oda | `Core/DefInjected/NeedDef/Needs.xml` (`RoomSize.label` **oda** boyutu) |

### Designators / order verbs (bare 2nd-person imperative, exactly as the game writes buttons)

| English | Türkçe | Source file |
|---|---|---|
| cancel | İptal et | `Core/Keyed/Designators.xml` (`DesignatorCancel`) |
| mine | Kaz | `Core/Keyed/Designators.xml` (`DesignatorMine`) |
| mine vein | Damarı kaz | `Core/Keyed/Designators.xml` (`DesignatorMineVein`) |
| harvest | Hasat et | `Core/Keyed/Designators.xml` (`DesignatorHarvest`) |
| chop wood / harvest wood | Ağaç kes | `Core/Keyed/Designators.xml` (`DesignatorHarvestWood`) |
| cut plants | Bitkileri kes | `Core/Keyed/Designators.xml` (`DesignatorCutPlants`) |
| deconstruct | Yık | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| uninstall | Kaldır | `Core/Keyed/Designators.xml` (`DesignatorUninstall`) |
| haul | Taşı | `Core/Keyed/Designators.xml` (`DesignatorHaulThings`) |
| hunt | Avla | `Core/Keyed/Designators.xml` (`DesignatorHunt`) |
| tame | Evcilleştir | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | Katlet | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| release to wild | Doğaya sal | `Core/Keyed/Designators.xml` (`DesignatorReleaseAnimalToWild`) |
| forbid | Yasakla | `Core/Keyed/Designators.xml` (`DesignatorForbid`) |
| unforbid / allow | İzin ver | `Core/Keyed/Designators.xml` (`DesignatorUnforbid`) |
| claim | Sahiplen | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| strip | Soy | `Core/Keyed/Designators.xml` (`DesignatorStrip`) |
| open (container) | Aç | `Core/Keyed/Designators.xml` (`DesignatorOpen`) |
| smooth (surface) | Yüzeyi düzleştir | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface`) |
| plan | Planla | `Core/Keyed/Designators.xml` (`DesignatorPlan`) |
| study / inspect | İncele | `Core/Keyed/Designators.xml` (`DesignatorStudy`) |
| extract tree | Ağaç sök | `Core/Keyed/Designators.xml` (`DesignatorExtractTree`) |
| remove floor | Zemini sök | `Core/Keyed/Designators.xml` (`DesignatorRemoveFloor`) |
| expand (area/zone) | genişlet | `Core/Keyed/Designators.xml` (`DesignatorZoneExpand` Alanı genişlet) |
| shrink (area/zone) | daralt | `Core/Keyed/Designators.xml` (`DesignatorZoneDeleteSingular` Alanı daralt) |
| tend (a wound) | Tedavi et | `Core/Keyed/FloatMenu.xml` (`Tend` **Tedavi et: {0}**) |
| rescue | Kurtar | `Core/Keyed/FloatMenu.xml` (`Rescue` Kurtar: {1_label}) |
| arrest | Tutukla | `Core/Keyed/FloatMenu.xml` (`Arrest` Tutukla: {0}) |
| capture | Esir al | `Core/Keyed/FloatMenu.xml` (`Capture` Esir al: {1_label}) |
| equip | Kuşan | `Core/Keyed/FloatMenu.xml` (`Equip` Kuşan: {0}) |
| attack | Saldır | `Core/Keyed/FloatMenu.xml` (`Attack` Saldır: {1_labelShort}) |
| clean | Temizle | `Core/Keyed/FloatMenu.xml` (`CleanRoom` Temizle: {0}) |
| assign / man (a turret) | Ata | `Core/Keyed/FloatMenu.xml` (`OrderManThing` Ata: {1_labelShort}) |
| paint | boya | `Core/Keyed/Designators.xml` (`DesignatorPaintBuilding` Yapıyı boya...) |
| mark (designate) | işaretle | `Core/Keyed/Designators.xml` (`DesignatorMineDesc` …kazılması için **işaretle**) |

### Buildings, zones, areas, build categories

| English | Türkçe | Source file |
|---|---|---|
| growing zone | Tarım alanı | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone`) |
| stockpile / storage zone | Depo alanı | `Core/Keyed/Misc_Gameplay.xml` (`Stockpile`); **depo** for the bare noun (`DesignatorZoneCreateStorageResourcesDesc` bir **depo** oluştur) |
| dumping stockpile | çöplük | `Core/Keyed/Designators.xml` (`DesignatorZoneCreateStorageDumpingDesc` bir **çöplük** oluştur) |
| home area | Ev alanı | `Core/Keyed/Designators.xml` (`DesignatorAreaHomeExpand` **Ev alanını** genişlet) |
| allowed area | İzinli alan | `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed`); `Core/Keyed/MainTabs.xml` (`AllowedArea`) |
| structure (build category) | yapı | `Core/DefInjected/DesignationCategoryDef/*` (`Structure.label`); `Ideology/Keyed/Misc_Gameplay.xml` (`Structure` Yapı) |
| furniture | mobilya | same file (`Furniture.label`) |
| floors | zeminler | same file (`Floors.label`) |
| power | güç | same file (`Power.label`) |
| production | üretim | same file (`Production.label`) |
| security | güvenlik | same file (`Security.label`) |
| temperature (category) | sıcaklık | same file (`Temperature.label`) |
| orders | emirler | same file (`Orders.label`) |
| recreation (category) | eğlence | same file (`Joy.label`) |
| misc | çeşitli | same file (`Misc.label`) |
| ship | gemi | same file (`Ship.label`) |
| architect menu | Mimari menüsü | `Ideology/Keyed/Alerts.xml` (`IdeoBuildingArchitectTabDesc` **Mimari menüsünün** '{TABNAME}' sekmesi) |
| roof | çatı | `Core/Keyed/Designators.xml` (`DesignatorAreaBuildRoofExpand` Çatı inşa et) |

### Work, priorities, skills, needs

| English | Türkçe | Source file |
|---|---|---|
| work tab | İş sekmesi | `Biotech/DefInjected/ConceptDef/Tutor.xml` (`Babies.helpText` **İş sekmesinden** birini çocuk bakımına ata) |
| work type / skill name | use the specific game label (madenci, doktor, aşçı…) | `Core/DefInjected/WorkTypeDef/WorkTypes.xml`, `Core/DefInjected/SkillDef/Skills.xml` |
| priority | Öncelik | `Core/Keyed/Misc_Gameplay.xml` (`Priority`) |
| manual priorities | Manuel öncelikler | `Core/Keyed/MainTabs.xml` (`ManualPriorities`) |
| priority 0–4 | Yapmayacak / En yüksek öncelikli / Yüksek öncelikli / Normal öncelikli / Düşük öncelikli | `Core/Keyed/MainTabs.xml` (`Priority0`…`Priority4`) |
| skill level | beceri seviyesi | `Core/Keyed/Skills.xml` (`SkillLevel`) |
| level | Seviye | `Core/Keyed/Skills.xml` (`Level`) |
| experience | Deneyim | `Core/Keyed/Skills.xml` (`Experience`) |
| passion | Şevk | `Core/Keyed/Skills.xml` (`Passion`) |
| passion — none / minor / major | Yok / İlgili / Çok tutkulu | `Core/Keyed/Skills.xml` (`PassionNone`, `PassionMinor`, `PassionMajor`) |
| trait | kişisel özellik | `Core/Keyed/Skills.xml` (`TraitLabelWithDesc` {TRAITLABEL} **kişisel özelliği**) |
| backstory | özgeçmiş | `Core/Keyed/MainTabs.xml` (`WorkDisabledByBackstory` **Özgeçmiş** yüzünden devre dışı) |
| mood | Ruh hâli | `Core/Keyed/Misc_Gameplay.xml` (`Mood`); `Core/DefInjected/NeedDef/Needs.xml` (`Mood.label` ruh hâli) |
| needs tab | İhtiyaçlar sekmesi | `Biotech/DefInjected/ConceptDef/Tutor.xml` (`Children.helpText` …**İhtiyaçlar sekmesinde** görebilirsin) |
| food (need) | beslenme | `Core/DefInjected/NeedDef/Needs.xml` (`Food.label`) |
| recreation / joy (need) | eğlence | same file (`Joy.label`) |
| rest | dinlenme | same file (`Rest.label`) |
| comfort | konfor | same file (`Comfort.label`) |
| beauty | güzellik | same file (`Beauty.label`) |
| indoors / outdoors | kapalı alan / açık hava | same file (`Indoors.label`, `Outdoors.label`) |
| room size | oda boyutu | same file (`RoomSize.label`) |
| apparel | Giyim | `Core/Keyed/Misc_Gameplay.xml` (`Apparel`) |
| forced apparel | Zorunlu giyim | `Core/Keyed/MainTabs.xml` (`ForcedApparel`) |
| policy (apparel/drug/food) | politika | `Core/Keyed/MainTabs.xml` (`ManageDrugPolicies` Uyuşturucu **politikalarını** yönet) |

### Health

| English | Türkçe | Source file |
|---|---|---|
| health | Sağlık | `Core/Keyed/Menus_Main.xml` (`Health`); `Core/Keyed/ITabs.xml` (`TabHealth` Sağlık) |
| overview | Genel bakış | `Core/Keyed/ITabs.xml` (`HealthOverview`) |
| bleeding (rate) | Kanama | `Core/Keyed/ITabs.xml` (`BleedingRate`) |
| tend (verb) | tedavi et | `Core/Keyed/FloatMenu.xml` (`Tend` Tedavi et: {0}) |
| tend quality | Tedavi kalitesi | `Core/Keyed/Misc_Gameplay.xml` (`TendQuality`) |
| self-tend | kendini tedavi | `Core/Keyed/FloatMenu.xml` (`SelfTendDisabled` **kendini tedavi** devre dışı) |
| medical care | Tıbbi bakım | `Core/Keyed/FloatMenu.xml` (`MedicalCareDisabled` **Tıbbi bakım** devre dışı) |
| medical (bed) | Tıbbi | `Core/Keyed/GameplayCommands.xml` (`CommandBedSetAsMedicalLabel`) |
| surgery / operation | ameliyat | `Biotech/DefInjected/RecipeDef/Recipes_Surgery_Misc.xml` (`Vasectomy.description` **Ameliyat** başarısız olursa…) |
| operations tab | operasyonlar sekmesi | `Biotech/Keyed/Letters.xml` (`LetterXenogermOrderedImplanted` sağlık/**operasyonlar sekmesine**) |
| wound | yara | `Biotech/Keyed/Dialogs_Various.xml` (`NumWoundsTended` {0} **yara** tedavi edildi) |
| pain | Acı | `Core/Keyed/Menus_Main.xml` (`Pain`) |
| hediff / body part | use the specific `HediffDef` / `BodyPartDef` label | `*/DefInjected/HediffDef/*`, `Core/DefInjected/BodyPartDef/*` |

### Trade

| English | Türkçe | Source file |
|---|---|---|
| trade (verb) | Ticaret yap | `Core/Keyed/Incidents.xml` (`CaravanMeeting_Trade`); `Core/Keyed/FloatMenu.xml` (`TradeWith` **Ticaret yap: {0}**) |
| trader | tüccar | `Core/Keyed/FloatMenu.xml` (`TraderDismissed` **Tüccar** kovuldu) |
| silver | gümüş | `Core/Keyed/Dialogs_Various.xml` (`YourTradeableSilver` Alışverişte kullanılabilir **gümüşün**: {0}) |
| price — very cheap / cheap / normal | çok ucuz / ucuz / normal | `Core/Keyed/Enums.xml` (`PriceTypeVeryCheap`, `PriceTypeCheap`, `PriceTypeNormal`) |
| market value | use the game's `StatDef` label | `Core/DefInjected/StatDef/*` |

### Research

| English | Türkçe | Source file |
|---|---|---|
| research | Araştırma | `Core/Keyed/MainTabs.xml` (`Research`) |
| research project | araştırma projesi | `Core/Keyed/Dialogs_Various.xml` (`ResearchUnknownProjectRevealed` …{1} **araştırma projesini** keşfettin) |
| research progress | Araştırma ilerlemesi | `Core/Keyed/Misc_Gameplay.xml` (`ResearchProgress`) |
| techprint | teknobaskı | `Core/Keyed/Misc.xml` (`TechprintLabel`); `Core/Keyed/MainTabs.xml` (`ResearchTechprintRequirement` Gereken **teknobaskılar** {0} / {1}) |
| tech level | teknoloji seviyesi | `Core/Keyed/MainTabs.xml` (`TechLevelTooLow` Fraksiyon **teknoloji seviyen** {0}…) |
| tech level — animal | vahşi | `Core/Keyed/Misc.xml` (`TechLevel_Animal`) |
| tech level — neolithic | neolitik | `Core/Keyed/Misc.xml` (`TechLevel_Neolithic`) |
| tech level — medieval | orta çağ | `Core/Keyed/Misc.xml` (`TechLevel_Medieval`) |
| tech level — industrial | sınai | `Core/Keyed/Misc.xml` (`TechLevel_Industrial`) |
| tech level — spacer | uzay | `Core/Keyed/Misc.xml` (`TechLevel_Spacer`) |
| tech level — ultra | ultra | `Core/Keyed/Misc.xml` (`TechLevel_Ultra`) |
| tech level — archotech | arkotek | `Core/Keyed/Misc.xml` (`TechLevel_Archotech`) |

### Prisoner, slave, ideology, ritual, abilities

| English | Türkçe | Source file |
|---|---|---|
| prisoner | Mahkûm / mahkûm | `Core/Keyed/Misc_Gameplay.xml` (`Prisoner` Mahkûm, `PrisonerLower` mahkûm, `PrisonersLower` mahkûmlar) |
| for prisoners (bed) | Mahkûmlar için | `Core/Keyed/GameplayCommands.xml` (`CommandBedSetForPrisonersLabel`) |
| recruitment resistance | Direnç | `Core/Keyed/ITabs.xml` (`RecruitmentResistance`, `RecruitmentResistanceDesc`) |
| recruit (join the colony) | koloniye katma / koloniye al | `Core/Keyed/Dialogs_Various.xml` (`RecruitDifficulty` **Koloniye katma** zorluğu); `Core/Keyed/ITabs.xml` (`RecruitmentResistanceDesc` …**koloniye alınabilir**) |
| warden (work type) | gardiyan | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.labelShort` gardiyan, `Warden.pawnLabel` Gardiyan) |
| slave | Köle | `Core/Keyed/ITabs.xml` (`Slave`) |
| enslave | köleleştir | `Biotech/Keyed/Letters.xml` (`Enslave`) |
| ideoligion | ideodin | `Ideology/Keyed/Misc_Gameplay.xml` (`Ideo` ideodin) |
| precept | öğreti / Öğretiler | `Ideology/Keyed/Misc_Gameplay.xml` (`Precept` öğreti); `Ideology/Keyed/MainTabs.xml` (`Precepts` Öğretiler) |
| meme | İlkeler (pl.) | `Ideology/Keyed/MainTabs.xml` (`Memes` İlkeler) |
| ritual | Ritüel / ayin | `Ideology/Keyed/MainTabs.xml` (`Rituals` Ritüeller). **ayin** is the head noun used when a ritual name is interpolated: `Ideology/Keyed/*` ({RITUAL_definite} **ayinini**, 13×) |
| role | Rol | `Ideology/Keyed/Dialogs_Various.xml` (`Role`) |
| believer | inanan | `Ideology/Keyed/Misc_Gameplay.xml` (`IdeoBuildingMissingDesc` …şu **inananlar** mutsuz olacak) |
| ability / power (psycast) | güç / yeti | `Royalty/Keyed/Misc_Gameplay.xml` (`AbilityNeurotrainerUsed` …{1} **gücünü** öğrenmek için); `Royalty/Keyed/Dialogs_Various.xml` (`PsylinkTooLowForGainAbility` …{ABILITY} **yetisini** kullanabilmek için) |
| psycast (the skill) | psi-becerisi | `Royalty/Keyed/Misc_Gameplay.xml` (`PsycastNeurotrainerUseLabel` {0} **psi-becerisini** öğrenmek için) |
| psyfocus | Psi-odak | `Royalty/Keyed/Misc_Gameplay.xml` (`Psyfocus`, `PsyfocusLabelGizmo`) |
| psylink | psi-bağ | `Royalty/Keyed/Misc_Gameplay.xml` (`PsycastNeurotrainerNoPsylink` **Psi-bağ** gerekli) |
| psychic | psişik | `Royalty/Keyed/Dialogs_Various.xml` (`AbilityTargetPsychicallyDeaf` Hedef **psişik** açıdan sağır) |

### Weather, season, time, dates

| English | Türkçe | Source file |
|---|---|---|
| temperature | Sıcaklık | `Core/Keyed/Misc_Gameplay.xml` (`Temperature`) |
| weather — clear / fog / rain / thunderstorm | açık / sis / yağmur / yağmurlu fırtına | `Core/DefInjected/WeatherDef/Weathers.xml` (`Clear.label`, `Fog.label`, `Rain.label`, `RainyThunderstorm.label`) |
| weather — light / heavy snow | hafif kar / ağır kar | same file (`SnowGentle.label`, `SnowHard.label`) |
| season — spring / summer / fall / winter | İlkbahar / yaz / sonbahar / kış | `Core/Keyed/Time.xml` (`SeasonSpring`…`SeasonWinter`) |
| quadrum (the unit) | çeyrek | `Core/Keyed/Dates.xml` (`DateReadoutTip` Mevcut **çeyrek**: {4}) |
| quadrum names | Nismay / Temmos / Kaylül / Ocalık | `Core/Keyed/Time.xml` (`QuadrumAprimay`…`QuadrumDecembary`) |
| day(s) / hour(s) | gün / saat | `Core/Keyed/Time.xml` (`DaysLower` gün, `HoursLower` saat) |
| letter abbreviations (d/h/m/s/y) | g / s / d / sn / y | `Core/Keyed/Time.xml` (`LetterDay`, `LetterHour`, `LetterMinute`, `LetterSecond`, `LetterYear`) |
| clock time / date / year | Saat / Tarih / Yıl | `Core/Keyed/Dates.xml` (`ClockTime`, `ClockDate`, `ClockYear`) |
| "{0} ago" | {0} önce | `Core/Keyed/Time.xml` (`TimeAgo`) |

### Gizmo / command / button labels

| English | Türkçe | Source file |
|---|---|---|
| draft (combat mode) | Çatışma Modu | `Core/Keyed/GameplayCommands.xml` (`CommandDraftLabel`) |
| undraft (normal mode) | Normal Mod | `Core/Keyed/GameplayCommands.xml` (`CommandUndraftLabel`) |
| fire at will | Ateş serbest | `Core/Keyed/GameplayCommands.xml` (`CommandFireAtWillLabel`) |
| hold fire | Ateş kes | `Core/Keyed/GameplayCommands.xml` (`CommandHoldFire`) |
| toggle power | Gücü aç/kapat | `Core/Keyed/GameplayCommands.xml` (`CommandTogglePowerLabel`) |
| hold open (door) | Açık tut | `Core/Keyed/GameplayCommands.xml` (`CommandToggleDoorHoldOpen`) |
| forbid passage | Geçişi yasakla | `Core/Keyed/GameplayCommands.xml` (`CommandToggleDoorForbid`) |
| select | Seç | `Core/Keyed/GameplayCommands.xml` (`CommandSelectStoredThing` **Seç: {0_label}**) |
| copy / paste | Kopyala / Yapıştır | `Core/Keyed/MainTabs.xml` (`Copy`, `Paste`) |
| rename | Yeniden adlandır | `Core/Keyed/Misc_Gameplay.xml` (`Rename`) |
| edit | Düzenle | `Core/Keyed/MainTabs.xml` (`AssignTabEdit`) |
| range (search radius) | yarıçap | `Core/Keyed/Dialogs_Various.xml` (`IngredientSearchRadius` Malzeme **yarıçapı**) |

### UI chrome

| English | Türkçe | Source file |
|---|---|---|
| accept | Kabul et | `Core/Keyed/Dialogs_Various.xml` (`AcceptButton`) |
| cancel | İptal et | `Core/Keyed/Dialogs_Various.xml` (`CancelButton`) |
| close | Kapat | `Core/Keyed/Dialogs_Various.xml` (`CloseButton`) |
| confirm | Onayla | `Core/Keyed/Dialogs_Various.xml` (`Confirm`) |
| OK | Tamam | `Core/Keyed/Dialogs_Various.xml` (`OK`) |
| yes / no | Evet / Hayır | `Core/Keyed/Misc.xml` (`Yes`, `No`) |
| back | Geri | `Core/Keyed/Menus_Main.xml` (`Back`) |
| next | İleri | `Core/Keyed/Menus_Main.xml` (`Next`) |
| options / settings | Ayarlar | `Core/Keyed/Menus_Main.xml` (`Options`) |
| menu | Menü | `Core/Keyed/PlayInterface.xml` (`Menu`) |
| enable / disable | Etkinleştir / Devre dışı bırak | `Core/Keyed/Menus_Main.xml` (`Enable`, `Disable`) |
| enabled / disabled (state) | Etkin / Devre dışı | `Core/Keyed/Menus_Main.xml` (`Enabled`, `Disabled`); lowercase `Core/Keyed/Misc.xml` (`DisabledLower` devre dışı) |
| add / remove | Ekle / Kaldır | `Core/Keyed/Dialogs_Various.xml` (`Add`); `Core/Keyed/FloatMenu.xml` (`Remove`) |
| delete | Sil | `Core/Keyed/Menus_Main.xml` (`Delete`) |
| save / load | Kaydet / Yükle | `Core/Keyed/Menus_Main.xml` (`Save`, `Load`) |
| reset | Sıfırla | `Core/Keyed/Dialogs_Various.xml` (`Reset`) |
| reset to default | Varsayılana sıfırla | `Core/Keyed/Menu_KeyBindings.xml` (`ResetBinding`) |
| clear | Temizle | `Core/Keyed/Menu_KeyBindings.xml` (`ClearBinding`) |
| default | Varsayılan / varsayılan | `Core/Keyed/Misc.xml` (`Default`, `default`) |
| none | Yok | `Core/Keyed/ITabs.xml` (`None`); parenthesized `Core/Keyed/MainTabs.xml` (`NoneBrackets` (hiç)) |
| all | tümü | `Ideology/Keyed/MainTabs.xml` (`All`) |
| name | İsim | `Ideology/Keyed/MainTabs.xml` (`Name`) |
| description | Açıklama | `Core/Keyed/Misc_Gameplay.xml` (`Description`) |
| details | Ayrıntılar | `Core/Keyed/Dialogs_Various.xml` (`Details`) |
| filter | Filtre | `Core/Keyed/Misc_Gameplay.xml` (`Filter`) |
| search (verb) | ara | `Core/Keyed/Dialogs_Various.xml` (`SearchTheMap` Mevcut haritada **ara**) |
| searching | Aranıyor | `Core/Keyed/Dialogs_Various.xml` (`Searching`) |
| search results | {0} sonuç bulundu | `Core/Keyed/Dialogs_Various.xml` (`MapSearchResults`) |
| click | tıkla | `Ideology/Keyed/Alerts.xml` (`RitualJumpToTargets` …için **tıkla**) |
| press (a key) | bas | `Core/Keyed/Menu_KeyBindings.xml` (`PressAnyKeyOrEsc` Herhangi bir tuşa **bas**…) |
| key / key binding | tuş / tuş ataması | `Core/Keyed/Menu_KeyBindings.xml` (`KeyBindingOverwritten` **Tuş ataması** üzerine yazıldı: {0}) |
| UI (interface) | Arayüz | `Core/Keyed/Menu_Options.xml` (`UIVolume` **Arayüz** sesi, `UIScale` **Arayüz** ölçeği) |

---

## Section 2 — Accessibility-specific vocabulary (mod-coined)

These concepts RimWorld does NOT name. We lock a Turkish rendering here. Where the game's own UI
offers a match, we reuse it and cite it so users hear familiar wording; otherwise we follow the
Turkish screen-reader community convention (NVDA-tr, Windows Ekran Okuyucusu).

| English | Türkçe (locked) | Rationale / source |
|---|---|---|
| screen reader | ekran okuyucu | Standard Turkish term (NVDA-tr, Windows «Ekran Okuyucusu»). Universally understood |
| announce (verb) | seslendir | Established TTS verb in Turkish. Prefer over «duyur», which reads as a public announcement |
| announcement (noun) | seslendirme | Matches the verb above |
| navigate / navigation | gezin / gezinme | Standard Turkish UI term; NVDA-tr uses «gezinme» |
| cursor | imleç | RimWorld uses it: `Core/Keyed/Menu_Options.xml` (`ZoomToMouse` Fare **imlecine** yakınlaştır); `Core/Keyed/Misc_Gameplay.xml` (`ShowBeautyToggleButton` **imlecin** gösterdiği yerde) |
| map scanner | harita tarayıcısı | Mod-specific feature. «tarayıcı» is the literal, unambiguous rendering |
| search (the act) | arama | RimWorld verb is **ara** (`SearchTheMap`); the noun is **arama** |
| typeahead search | yazarak arama | No fixed game term. «yazarak arama» (search by typing) is transparent in TTS |
| tree view | ağaç görünümü | Standard Turkish UI term; NVDA-tr announces tree controls as «ağaç» |
| node (tree node) | düğüm | Standard Turkish UI term; NVDA-tr uses «düğüm» |
| level (tree depth) | seviye | RimWorld uses it: `Core/Keyed/Skills.xml` (`Level` Seviye). e.g. «seviye {0}» |
| expand | genişlet | RimWorld uses it for areas: `Core/Keyed/Designators.xml` (`DesignatorZoneExpand` Alanı **genişlet**) |
| collapse | daralt | RimWorld's own counterpart to genişlet: `Core/Keyed/Designators.xml` (`DesignatorZoneDeleteSingular` Alanı **daralt**) |
| expanded (state) | genişletildi | Impersonal past passive; carries no gender or person |
| collapsed (state) | daraltıldı | Impersonal past passive |
| toggle (verb/control) | aç/kapat | RimWorld's own toggle wording, used ~12×: `Core/Keyed/GameplayCommands.xml` (`CommandTogglePowerLabel` Gücü **aç/kapat**), `CommandToggleDraftDesc`, `Core/Keyed/Misc_Gameplay.xml` (Kolonici barını **aç/kapat**). Keep the slash, no spaces |
| on / off (state) | açık / kapalı | Standard. For an ability/feature state prefer the game's **Etkin** / **Devre dışı** (`Core/Keyed/Menus_Main.xml`) |
| checkbox | onay kutusu | Standard Turkish UI term; NVDA-tr announces «onay kutusu» |
| checked / unchecked | işaretli / işaretsiz | Standard; pairs with onay kutusu. Note RimWorld uses **işaretle** for designating (Section 1) — the collision is harmless in context |
| slider | kaydırıcı | Standard Turkish UI term. Cognate with the game's **kaydırma** (`Core/Keyed/Menu_Options.xml` `EdgeScreenScroll` Ekran kenarında **kaydırma**) |
| scroll (verb / noun) | kaydır / kaydırma | `Core/Keyed/Menu_Options.xml` (`EdgeScreenScroll` Ekran kenarında **kaydırma**) |
| drag | sürükle | RimWorld uses it in the UI sense: `Core/Keyed/Misc_Gameplay.xml` (`ShowColonistBarToggleButton` Sağ tıka basılı tutup **sürükleyerek** ikonların… yerlerini değiştirebilirsin) |
| tab (UI tab) | sekme | RimWorld uses it: `Biotech/DefInjected/ConceptDef/Tutor.xml` (**İş sekmesinden**), `Biotech/Keyed/Letters.xml` (operasyonlar **sekmesine**), `Ideology/Keyed/Alerts.xml` ('{TABNAME}' **sekmesini**) |
| button (UI) | düğme | Standard Turkish UI term. NOTE: the corpus's **buton** (`Core/Keyed/Menu_KeyBindings.xml` `PressAnyKeyOrEscController`) means a *gamepad* button — do not reuse it for on-screen buttons |
| selected | seçili | RimWorld uses it: `Biotech/Keyed/Misc_Gameplay.xml` (`PersonSelected` Kişi **seçili**, `EmbryoSelected` Embriyo **seçili**) |
| unselected / not selected | seçili değil | Negation of the above; impersonal and gender-free |
| read-only | salt okunur | Standard Turkish computing term |
| focused | odakta | Standard. Cognate with the game's **Psi-odak** (`Royalty/Keyed/Misc_Gameplay.xml` `Psyfocus`) |
| row / column | satır / sütun | Standard Turkish UI terms |
| list | liste | Standard |
| position ("X of Y") | {0} / {1} | RimWorld's own count-of-total shape: `Core/Keyed/MainTabs.xml` (`ResearchTechprintRequirement` Gereken teknobaskılar **{0} / {1}**; `PregnantIconDesc` Hamile (**{0} / {1}**)). Spell it out as «{1} öğeden {0}.» only where a bare ratio would be ambiguous in speech |
| jump to | Git: {0} | Built from RimWorld's **Git**- (`Core/Keyed/MainTabs.xml` `ClickToJumpTo` **Gitmek** için tıkla; `Ideology/Keyed/Alerts.xml` `RitualJumpToTargets` Hedeflere **atlamak** için tıkla) in the colon shape of Section 3.1 |
| edge / boundary | Zaten en üstte / Zaten en altta | "Already at top/bottom". «en üstte» / «en altta» are the plain Turkish edge words |
| stepper (numeric +/- control) | değer ayarlayıcı | No game term and no fixed Turkish convention. «değer ayarlayıcı» (value adjuster) is unambiguous in speech; for the bounds reuse **en az** / **en çok** |
| hotkey | kısayol tuşu | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` **Kısayol tuşu:** {0}); bare form `HotKeyTip` Kısayol |
| accessibility | erişilebilirlik | Standard Turkish term |
| inspect / inspection | incele / inceleme | RimWorld uses it: `Core/Keyed/Designators.xml` (`DesignatorStudy` **İncele**); `Core/Keyed/GameplayCommands.xml` (`CommandToggleStudy` **İncelemeyi** aç/kapat) |

---

## Section 3 — Style rules for translators

### 3.1 Suffixes after placeholders (THE critical Turkish decision)

Turkish attaches case suffixes to the end of a noun, and the suffix's vowels must harmonize with the
**last vowel of that noun** — `{0}'ı` vs `{0}'i` vs `{0}'u` vs `{0}'ü`. The word inside `{0}` arrives
at runtime from a def label or a pawn name, so **we cannot know which suffix is correct.** Choosing
one produces wrong Turkish for most values, and a screen reader speaks that error out loud.

**Evidence — the official Turkish translation refuses to suffix placeholders.** Of **11,220**
placeholder occurrences in the corpus, only about **a dozen** carry a Turkish case suffix
(`{1}'in`, `{1}'den`, `{0}'dan`, `{0}'lerde`, `{PAWN_nameDef}'in`, `{PAWN_labelShort}'in` — e.g.
`Core/Keyed/Menus_Main.xml` `ModLoadFolderOutOfOrder` «{0}, {1}'den önce gelmeli», and
`Core/Keyed/Messages.xml` `MessageBillValidationStoreZoneDeleted` «{1}'in "{0}" öğesi…»). Every one
of those sits where the translator happened to know the inserted word. The 276 other
`{placeholder}'…` hits are **untranslated English possessives** (`{PAWN_nameDef}'s`, 53×) left in the
file, not a Turkish convention.

**Instead the corpus uses three strategies. Use these, in this order of preference.**

**Strategy A — the colon shape (`Fiil: {0}`). This is the workhorse; prefer it.**

RimWorld's Turkish systematically rewrites English "Verb {0}" into "Verb**: **{0}", leaving the
placeholder bare and nominative. The corpus's own `<!-- EN: … -->` comments make the transformation
explicit:

| English source (corpus comment) | Turkish (corpus value) | Key / file |
|---|---|---|
| `Attack {1_labelShort}` | `Saldır: {1_labelShort}` | `Attack`, `Core/Keyed/FloatMenu.xml` |
| `Rescue {1_labelShort}` | `Kurtar: {1_label}` | `Rescue`, same file |
| `Arrest {0}` | `Tutukla: {0}` | `Arrest`, same file |
| `Capture {1_labelShort}` | `Esir al: {1_label}` | `Capture`, same file |
| `Equip {0}` | `Kuşan: {0}` | `Equip`, same file |
| `Consume {1_labelShort}` | `Tüket: {1_labelShort}` | `ConsumeThing`, same file |
| `Clean {0}` | `Temizle: {0}` | `CleanRoom`, same file |
| `Call {0}` | `Ara: {0}` | `CallOnRadio`, same file |
| `Man {1_labelShort}` | `Ata: {1_labelShort}` | `OrderManThing`, same file |
| `Cannot pick up {1_labelShort}` | `Alamaz: {1_labelShort}` | `CannotPickUp`, same file |
| `Upgrade {0} to level {1}` | `{1} seviyesine yükselt: {0}` | `UpgradeImplant`, same file |
| — | `Tedavi et: {0}` | `Tend`, same file |
| — | `Seç: {0_label}` | `CommandSelectStoredThing`, same file |
| — | `Görevi görüntüle: {0}` | `CommandViewQuest`, same file |
| — | `Ticaret yap: {0}` | `TradeWith`, same file |

The pattern appears **353×** across the corpus. It fits our mod perfectly: most of our strings are
already `label: value` pairs, and the colon is a clean TTS pause. **When in doubt, use the colon.**

**Strategy B — a postposition, which is a separate word and needs no suffix.**

Turkish postpositions attach to nothing, so the placeholder stays bare:

- **ile** (with) — 118× after a placeholder. `Core/Keyed/FloatMenu.xml` (`TradeWithSettlement`
  «{0} **ile** ticaret yap»)
- **için** (for) — 69×. `Ideology/Keyed/Alerts.xml` (`RitualOpportunityFor` «{1} **için** {0} fırsatı»)
- **tarafından** (by) — 128×. `Core/Keyed/Incidents.xml` (`CaravanDemandTitle` «{0} **tarafından** kervan talebi»)
- **içinde** (in / within) — 83×. `Core/Keyed/Alerts.xml` (`QuestPartShuttleLeaveDelay` «Mekik {0} **içinde** kalkıyor»)

**Strategy C — an appositive head noun that carries the suffix instead.**

Put a generic noun after the placeholder and inflect *that* noun. Its vowels are known at translation
time, so the harmony is always right. The corpus does this constantly:

| Head noun | Meaning | Corpus example |
|---|---|---|
| **kişi** | person | «{PAWN_nameDef} **kişisine** bir zenogerm aşılanmasını emrettin» (`Biotech/Keyed/Letters.xml` `LetterXenogermOrderedImplanted`); «{0_nameDef} **kişisinin** köle statüsü» (`CannotChangeChildStatusReason`) |
| **öğe** | item | «{1}**'in** "{0}" **öğesi** artık silinen…» (`Core/Keyed/Messages.xml` `MessageBillValidationStoreZoneDeleted`); «{0} **öğesini**», «{1} **öğesine**» |
| **adlı** + noun | named … | «{PAWN_nameDef} **adlı kişinin** genlerini görmek için tıkla» (`Biotech/Keyed/ITabs.xml` `ViewGenesDesc`); «{BABY_labelShort} **adlı bebeği**» (`AutofeedModeTooltipChildcare`) — 51× |
| **fraksiyon** | faction | «{0} **fraksiyonundan** ultra ağır bir mekanoit» (`Biotech/Keyed/Letters.xml` `LetterBossgroupArrived`) — 106× |
| **bölge** | region | «{1} **bölgesindeki** kirlilik {0} arttı» (`Biotech/Keyed/Messages.xml` `MessageWorldTilePollutionChanged`) |
| **ayin** | ritual | «{RITUAL_definite} **ayinini**» (`Ideology/Keyed/*`) — 13× |
| **gen** | gene | «{GENE_label} **geni**» (`Core/Keyed/Skills.xml` `GeneLabelWithDesc`) |
| **kişisel özellik** | trait | «{TRAITLABEL} **kişisel özelliği**» (`Core/Keyed/Skills.xml` `TraitLabelWithDesc`) |

For our mod the useful head nouns are **öğe** (a list item / element), **kişi** (a pawn), **adlı**
(when quoting a name), **düğme** (a button), **sekme** (a tab), **bölge** / **alan** (a zone or area).

**What NOT to do:**

| Bad (guesses a suffix) | Why it breaks | Good |
|---|---|---|
| `{0}'ı seçtin` | wrong for «kolonici», «gümüş», «öğreti» | `Seçildi: {0}` |
| `{0}'a git` | wrong for «depo», «mahkûm» | `Git: {0}` |
| `{0}'nin seviyesi` | wrong for most labels | `{0} öğesinin seviyesi` or `Seviye: {0}` |
| `{0}'den {1} tane` | double guess | `{0} / {1}` |

**Apostrophes:** the only apostrophe use we keep is the corpus's quoting of an interpolated name —
`'{0}'` (27×) and `"{0}"` (19×), e.g. «'{TABNAME}' sekmesini» (`Ideology/Keyed/Alerts.xml`
`IdeoBuildingArchitectTabDesc`). Never write an apostrophe to attach a case suffix to a placeholder.

### 3.2 Plurals — flat singular after a numeral; `_numCase` is FORBIDDEN

**Finding: the Turkish corpus contains ZERO `_numCase` usages.** Verified over the whole extraction
(Core + all five DLCs, `Keyed/` and `DefInjected/`):

```
$ grep -rho '_numCase' <tr_ref>/ | wc -l
0
```

**So the mod must never use `_numCase` in Turkish.** `GrammarResolverSimple.ResolveNumCase` checks the
branch count against `activeLanguage.info.totalNumCaseCount ?? activeLanguageWorker.TotalNumCaseCount`.
Turkish ships no `LanguageWorker` of its own, so it falls back to `LanguageWorker_Default`
(`TotalNumCaseCount` = 0) and Core's `LanguageInfo.xml` leaves `totalNumCaseCount` unset. A multi-branch
`_numCase` tag fails the count check, logs an error, and **returns an empty string** — a silently blank
spoken announcement. We cannot override this from a mod: `LoadedLanguage.LoadMetadata` takes the
**first** `LanguageInfo.xml` across `RunningMods` and returns, and mods are forced to load after Core,
so Core's Turkish metadata always wins.

**The good news: Turkish does not need it.** Turkish nouns take **no plural suffix after a numeral** —
the number already marks plurality, so «3 gün» is correct and «3 günler» is wrong. A single flat
singular form is not a compromise here, it is the grammatically correct answer for every count.

Corpus evidence (bare singular after a count placeholder):

- «{0} **gün**» — 29× (e.g. `Anomaly/DefInjected/ThingDef/Races_Entities_Misc.xml`
  `Nociosphere.comps.CompNociosphere.unstableWarning` «nosyoküre {0} gün boyunca…»)
- «{0} **kişi**» — 14×, «{1} kişi» — 9×
- «{0} **kolonici**» — 6×, «{0} **hayvan**» — 4×, «{0} **öğe**» — 5×, «{0} **saat**» — 3×,
  «{0} **yıl**» — 4×, «{0} **adet**» — 15×
- «{0} **mekanoit**» (`Biotech/Keyed/Misc_Gameplay.xml` `CaravanMechsCount`)
- «{0} **yara** tedavi edildi» (`Biotech/Keyed/Dialogs_Various.xml` `NumWoundsTended`)
- «{0} **sonuç** bulundu» (`Core/Keyed/Dialogs_Various.xml` `MapSearchResults`)

**THE RULE:** after a count placeholder, write the noun in the **bare singular**. Never add `-ler`/`-lar`.

| English | Türkçe |
|---|---|
| `{0} items` | `{0} öğe` |
| `{0} days` | `{0} gün` |
| `{0} colonists` | `{0} kolonici` |
| `{0} results` | `{0} sonuç` |
| `{0} characters` | `{0} karakter` |

Use `-ler`/`-lar` only where there is **no** numeral and the plural is inherent — «Mahkûmlar»
(`Core/Keyed/ITabs.xml` `PrisonersLower`), «Öğretiler», «Ritüeller».

#### One / Many key PAIRS

Some of our keys come in a `…One` / `…Many` pair, chosen in C# by `count == 1`. Turkish needs no
distinction: **give both keys the same text** (`{0} öğe`). Always fill **both** — leaving one blank
makes the mod fall back to English for that count.

### 3.3 Register — informal second person «sen»

RimWorld's Turkish addresses the player in the **informal second person singular**, throughout. It is
not a stylistic choice we can revisit; matching it is what makes the mod sound like the game.

Evidence:

- Possessive `-n`: «**kolonini** enkaza çevirmek için geldi» (`Biotech/Keyed/Letters.xml`
  `LetterBossgroupArrived`); «Fraksiyon teknoloji **seviyen** {0}» (`Core/Keyed/Dialogs_Various.xml`
  `TechLevelTooLow`); «Alışverişte kullanılabilir **gümüşün**» (`YourTradeableSilver`)
- Informal imperative: «**tıkla**», «**seç**», «**bas**», «**ata**», «Herhangi bir tuşa **bas**»
  (`Core/Keyed/Menu_KeyBindings.xml` `PressAnyKeyOrEsc`)
- Informal 2sg verb: «Devam etmek **istediğinden** emin misin?» (`Biotech/Keyed/Dialogs_Various.xml`
  `WarningPawnWillDieFromReimplanting`); «bir zenogerm aşılanmasını **emrettin**»; «Artık tüm
  dersleri sıfırdan **öğreneceksin**» (`Core/Keyed/Menu_Options.xml` `AdaptiveTutorIsReset`)

**Never use «siz» or the formal imperative («seçiniz», «tıklayınız»).**

Command labels are the **bare 2nd-person imperative**, not an infinitive and not a noun: «İptal et»,
«Kaz», «Taşı», «Avla», «Yasakla», «Kapat», «Kaydet» (`Core/Keyed/Designators.xml`,
`Core/Keyed/Menus_Main.xml`). Do not write «İptal etmek» or «İptal».

For states, use nouns or impersonal passives — «Ruh hâli», «Kanama», «Etkin», «Devre dışı»,
«genişletildi», «seçili». Impersonal passives (`-ildi`, `-ili`) carry no person, so they are safe when
the subject is unknown.

Otherwise match RimWorld's neutral, terse UI register. No exclamation marks unless the English has
them. No first person. No emoji.

### 3.4 Capitalization — the dotted/dotless i (SHARP)

Turkish has **two i's**, and their cases do not map the way ASCII does:

- `i` (dotted, U+0069) uppercases to **`İ`** (U+0130) — never to `I`
- `ı` (dotless, U+0131) uppercases to **`I`** (U+0049) — never to `İ`

The corpus is consistent about this: «**İ**ptal et», «**İ**leri», «**İ**ncele», «**İ**nşa»,
«**İ**zin ver», «**İ**sim», «**İ**lkbahar», «AZAM**İ**» (`Core/Keyed/Skills.xml` `SkillMax`),
«EVET»/«HAYIR» (`Core/Keyed/Misc.xml` `YesUppercase`, `NoUppercase`).

Type the correct letter directly. Never reason about casing as if it were ASCII, and never let a
find-and-replace convert `İ`→`I` or `ı`→`i`.

**Case style:** RimWorld's Turkish uses **sentence case** for labels and buttons — first word
capitalized, the rest lowercase: «Yeniden adlandır», «Devre dışı bırak», «Kar küreme alanı»,
«Ev alanını genişlet», «Genel bakış». Do **not** use English Title Case. The two exceptions the
corpus itself makes are proper-noun-like mode names («Çatışma Modu», «Normal Mod») and the two
DefInjected label conventions (many `*.label` values are all-lowercase because the game capitalizes
them at render time — e.g. `Furniture.label` «mobilya», `Food.label` «beslenme»). When you translate a
key whose English is lowercase, keep it lowercase.

### 3.5 Punctuation, numbers, quotation

- **Punctuation is Latin-style** (`. , : ; ! ?`) with no leading space. Nothing full-width.
- **Quotation marks:** the corpus uses straight `'…'` (392 occurrences) and `"…"` (214) — **no
  guillemets, no curly quotes.** Straight single quotes are the more common choice around an
  interpolated name or a UI label being referenced: «'{TABNAME}' sekmesini», «'Doğum için toplan'
  seçeneğine tıkla» (`Biotech/Keyed/Letters.xml` `LetterColonistPregnancyLabor`). Prefer `'…'`.
- **Circumflex letters are live in this corpus** — «mahkûm», «ruh hâli», «hâlinde», «rüzgâr»,
  «zanaatkâr», «dâhil». Copy them exactly; they are not typos and dropping them changes the word.
- **Numbers arrive pre-formatted through placeholders. Never reformat them.** Turkish convention is a
  comma decimal separator, but the game formats the value before it reaches the string — do not add,
  remove, or convert separators inside `{0}`.
- **Percent signs:** do not add one. The game supplies percentages already formatted. Keep whatever
  the English source has; the corpus is itself mixed («%{0}» once, «{0}%» five times), so there is no
  convention to follow.
- **Composed fragment joiners stay ASCII.** The mod glues TTS segments with `". "`, `", "`, `": "`
  exactly like English (e.g. `RimWorldAccess.TwoLevel.ButtonAnnouncement` = `{0}. Button. {1}`). Keep
  these as an ASCII period/comma/colon followed by a single space. TTS engines treat `. ` / `, ` /
  `: ` as reliable pause boundaries, and the trailing space safely separates an adjacent number or
  Latin token. Do not replace them with dashes or drop the space.

### 3.6 Placeholders, line breaks, keys, spacing

- Copy `{0}`, `{1}`, `{NamedArg}`, `{PAWN_label}`, `{lookup: …}` etc. **byte-for-byte.** Never
  translate, never add or remove braces, never renumber. The **set** of placeholders in a value must
  match the English source exactly.
- You **may reposition** a placeholder for natural Turkish word order — `string.Format` is positional.
  Turkish is verb-final, so this is often necessary and is encouraged: the corpus's `UpgradeImplant`
  reorders English «Upgrade {0} to level {1}» into «{1} seviyesine yükselt: {0}». Repositioning is
  fine; renumbering or translating is forbidden.
- Copy `\n` line breaks exactly and keep them in the same logical spots.
- **XML keys are never translated** — every element name stays byte-for-byte identical to English, or
  the string silently fails to load.
- **XML comments are developer context — leave them in English.** Do not translate `<!-- … -->`.
- **Preserve leading/trailing spaces in values.** Some keys are suffixes that join onto a preceding
  label (e.g. `RimWorldAccess.InfoCard.Inspectable` = `" Inspectable."` with a leading space;
  `RimWorldAccess.Menu.LevelSuffix` = `" level {0}"`). Keep the exact leading/trailing space.
- Keep a single normal space where a number or placeholder abuts Turkish text: «{0} öğe»,
  «seviye {0}». Do not glue them together, and do not insert a space before the `/` in «aç/kapat».

---

## Section 4 — How to extend this glossary

To find how RimWorld translates a term you don't see above (paths relative to the extracted corpus
root):

```bash
# UI strings (button labels, menu text, gameplay commands):
grep -rh 'EnglishKeyName' <tr_ref>/*/Keyed/*.xml

# Or search by a Turkish word you suspect:
grep -rh 'türkçe_kelime' <tr_ref>/*/Keyed/*.xml

# Concept labels (things, designators, factions, skills, needs, biomes…):
grep -rh 'EnglishKeyName' <tr_ref>/*/DefInjected/**/*.xml

# How the official translation handled a suffix-after-placeholder case:
grep -rnE "\{[A-Za-z0-9_]+\} (kişisi|öğesi|adlı|fraksiyonu)" <tr_ref>/
```

Useful sub-paths: `Keyed/Designators.xml` (order verbs), `Keyed/GameplayCommands.xml` (gizmos and
commands), `Keyed/FloatMenu.xml` (right-click actions — the richest source of the `Fiil: {0}` colon
pattern), `Keyed/Misc.xml` + `Keyed/Misc_Gameplay.xml` (general UI), `Keyed/Dialogs_Various.xml`
(dialog buttons), `Keyed/Menus_Main.xml` (main menu, enable/disable), `Keyed/Menu_Options.xml`
(settings vocabulary), `Keyed/Menu_KeyBindings.xml` (keys and bindings), `Keyed/Time.xml` +
`Keyed/Dates.xml` (calendar), `Keyed/Skills.xml`, `Keyed/ITabs.xml` (inspect tabs),
`Keyed/MainTabs.xml` (work/assign tabs), `DefInjected/WorkTypeDef`, `DefInjected/SkillDef`,
`DefInjected/NeedDef`, `DefInjected/DesignationCategoryDef`, `DefInjected/WeatherDef`.

Many corpus files carry `<!-- EN: … -->` comments giving the English source above each value. Use them
to confirm a key's meaning — and, for anything involving a placeholder, to see exactly how the
official translators restructured the English sentence. Always cite the file you took a term from
when you add a row.

## Mod-coined terms — watermill placement + read-only browsing (2026-08-13)

Thirteen new keys (`RimWorldAccess.Building.Place.Spot*`, `.Watermill*`, `RimWorldAccess.TextInput.BrowsingField/ReadOnlyField`)
had no reference translation to check: this local install only carries the English Core language pack,
so RimWorld's own Turkish wording for the `WatermillGenerator` ThingDef could not be verified against
the game's corpus. Decided from context per house rule (no native-review parking); record here so later
waves stay consistent instead of re-deriving these. The Watermill keys keep the direction-before-count
word order already established by `OutlineOkAt` ("{3} yönünde {2} kare").

| English | Türkçe | Basis |
|---|---|---|
| watermill (whole building) | su değirmeni | Standard Turkish term for a water mill |
| waterwheel (the wheel part) | su çarkı | Standard Turkish term for a water wheel |
| moving/running water | akan su | Standard idiom for flowing water |
| water flow area | su akış alanı | Coined; parallels `Gerekli alan` (`OutlineOkAt`) |
| unshared / shared with another watermill | paylaşılmıyor / başka bir su değirmeniyle paylaşılıyor | Coined |
| placement spot (scanner category/item) | {0} yerleştirme alanları / Yer | Mirrors `Abilities.Plant.ScannerCategory`/`ScannerItemLabel` ("{0} ekim alanları" / "Ekilebilir yer") |
| facing {0} (trailing fragment) | {0} yönünde | Reuses the postposition already established inline in `OutlineOkAt` |
| read only (BrowsingField) | salt okunur | Reuses the glossary's own locked term (§ read-only row) |
