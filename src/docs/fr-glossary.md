# French (Français) Terminology & Style Glossary — RimWorld Access

This glossary locks in consistent French wording for translating the RimWorld Access screen-reader
mod. The goal is that French players feel the mod is a seamless extension of RimWorld itself, so
**every game-anchored term below uses the EXACT word RimWorld's own official French translation
uses**, with a citation to the reference file it was taken from.

The strings we translate are **spoken aloud by a TTS engine**, not displayed. Natural, terse,
unambiguous phrasing matters more than visual polish.

Reference corpus: RimWorld's official French, extracted from `Core/Languages/French (Français).tar`
plus the Royalty, Ideology, Biotech, Anomaly and Odyssey equivalents. Paths below are relative to the
extracted corpus root (`Core/`, `Royalty/`, `Ideology/`, `Biotech/`, `Anomaly/`, `Odyssey/`).

> How to use this file: grep for the English term. If a term you need is not here and RimWorld has
> it, grep the game corpus yourself (see Section 4) and add it — never invent a rendering for a
> concept the game already names.

---

## Section 0 — Language folder name (READ FIRST)

**Ship the mod's French strings in `Languages/French/` — the pure-ASCII legacy name, NOT
`French (Français)`.**

Core ships the language as `French (Français).tar`, but a mod adding to an existing language is
merged by `LanguageDatabase.InitLanguageMetadataFrom`, which matches on
`folderName == langDir.Name || LegacyFolderName == langDir.Name`. `LegacyFolderName` is the part
before the `(` — i.e. `French`. Naming our folder with the native form would embed `ç` (U+00E7), a
non-ASCII character whose byte representation changes under Unicode normalization (NFC vs NFD). Zip
the mod on macOS, unzip on Windows, and the name can flip to a decomposed form that no longer
byte-matches Core's folder. The game then registers our folder as a **separate** `LoadedLanguage` and
the player sees **two** identical-looking French entries in the language menu.

`French` has no non-ASCII bytes, so it always matches Core's `LegacyFolderName` and merges correctly
on every platform. This matches every language the mod already ships (`Languages/Ukrainian/`,
`Languages/ChineseSimplified/`, `Languages/SpanishLatin/`, `Languages/Turkish/`). The menu still
displays «Français» — that comes from Core's entry, which our folder merges into. No
`LanguageInfo.xml` is needed.

Mirror `Languages/English/Keyed/` exactly: we ship **only** `Keyed/`.

---

## Section 1 — Core game vocabulary (game-anchored)

Every row is the term RimWorld itself ships in French. Use it verbatim. These are the words the
player already hears from the game, so our mod must match them exactly.

### People, factions, world

| English | Français | Source file |
|---|---|---|
| colonist | colon | `Core/Keyed/Misc.xml` (`Colonist` **colon**); plural «{0} **colons**» throughout, e.g. `Core/Keyed/Alerts.xml` |
| colony | colonie | `Core/Keyed/GameplayCommands.xml` (`CommandAttackSettlementDesc` Attaquer cette **colonie**.) |
| pawn / character (generic) | personnage | `Core/Keyed/GameplayCommands.xml` (`CommandFireAtWillDesc` …ce **personnage** ne tire pas…); also **personne** for the plain-human sense: `Biotech/Keyed/Misc_Gameplay.xml` (`PersonSelected` **Personne** sélectionnée) |
| faction | faction | `Core/Keyed/Misc_Gameplay.xml` (`Faction`) |
| settlement (world) | colonie / base de faction | `Core/Keyed/Letters.xml` (`LetterRelatedPawnsSettlement` Des personnes de cette **colonie**…, `LetterRelatedPawnsTradingWithSettlement` La **base de faction** vend…) |
| caravan | caravane | `Core/Keyed/Misc_Gameplay.xml` (`Caravan`); `Core/Keyed/Dialogs_Various.xml` (`FormCaravan` Former une **caravane**, `ReformCaravan` Reformer la **caravane**) |
| raid | raid | `Core/Keyed/Misc_Gameplay.xml` (`Raid`) — a genuine loanword here, do NOT write «attaque» |
| map | carte | `Core/Keyed/Misc_Gameplay.xml` (`Map`) |
| map cell / square | case | `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` …sur la même **case**) |
| world / planet | monde / planète | `Core/Keyed/Dialogs_Various.xml` (`SearchTheWorld` Rechercher dans le **monde**); `Core/DefInjected/MainButtonDef/*` (`World.label` **monde**) |
| area | zone | `Core/Keyed/Misc_Gameplay.xml` (`AreaLower` **zone**) |
| zone | zone | `Core/Keyed/Misc_Gameplay.xml` (`Zone` Zone); `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Zone.label` **zones**) |
| room | salle | `Core/Keyed/Misc_Gameplay.xml` (`Room` Salle) |
| biome | biome | `Core/Keyed/Menus_Main.xml` (`Biome`) |
| terrain | terrain | `Core/Keyed/Menus_Main.xml` (`Terrain`) |
| elevation | altitude | `Core/Keyed/Menus_Main.xml` (`Elevation`) |
| rainfall | précipitations | `Core/Keyed/Menus_Main.xml` (`Rainfall`) |
| forageability | possibilité de cueillette | `Core/Keyed/Menus_Main.xml` (`Forageability`) |
| goodwill | bonne entente | `Core/Keyed/Misc_Gameplay.xml` (`Goodwill`) |
| ally / enemy / neutral | alliée / ennemi / neutre | `Core/Keyed/MainTabs.xml` (`Ally`), `Core/Keyed/Misc_Gameplay.xml` (`Enemy`), `Core/Keyed/Letters.xml` (`Neutral`) |
| hilliness | plat / petite colline / grande colline / montagneux / infranchissable | `Core/Keyed/Enums.xml` (`Hilliness_Flat`…`Hilliness_Impassable`) |
| gravship | vaisseau gravitationnel | `Odyssey/Keyed/Misc_Gameplay.xml` (`Gravship` **Vaisseau gravitationnel**) — 122× across the corpus |
| storyteller | narrateur | `Core/Keyed/MainTabs.xml` (`Storyteller`) |
| mod | mod | `Core/Keyed/Menus_Main.xml` (`Mods`) |

### Designators / order verbs (INFINITIVE, exactly as the game writes buttons)

French command labels are **infinitives**, not imperatives and not nouns. This is the corpus's
uniform choice.

| English | Français | Source file |
|---|---|---|
| cancel | Annuler | `Core/Keyed/Designators.xml` (`DesignatorCancel`) |
| mine | Miner | `Core/Keyed/Designators.xml` (`DesignatorMine`) |
| mine vein | Miner une veine | `Core/Keyed/Designators.xml` (`DesignatorMineVein`) |
| harvest | Récolter | `Core/Keyed/Designators.xml` (`DesignatorHarvest`) |
| chop wood / harvest wood | Couper du bois | `Core/Keyed/Designators.xml` (`DesignatorHarvestWood`) |
| cut plants | Couper les plantes | `Core/Keyed/Designators.xml` (`DesignatorCutPlants`) |
| deconstruct | Démolir | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| uninstall | Désinstaller | `Core/Keyed/Designators.xml` (`DesignatorUninstall`) |
| haul | Transporter | `Core/Keyed/Designators.xml` (`DesignatorHaulThings`) |
| hunt | Chasser | `Core/Keyed/Designators.xml` (`DesignatorHunt`) |
| tame | Apprivoiser | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | Abattre | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| release to wild | Relâcher dans la nature | `Core/Keyed/Designators.xml` (`DesignatorReleaseAnimalToWild`) |
| forbid | Interdire | `Core/Keyed/Designators.xml` (`DesignatorForbid`) |
| unforbid / allow | Autoriser | `Core/Keyed/Designators.xml` (`DesignatorUnforbid`) |
| claim | Revendiquer | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| strip | Déshabiller | `Core/Keyed/Designators.xml` (`DesignatorStrip`) |
| open (container) | Ouvrir | `Core/Keyed/Designators.xml` (`DesignatorOpen`) |
| smooth (surface) | Aplanir | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface`) |
| plan | Planifier | `Core/Keyed/Designators.xml` (`DesignatorPlan`) |
| study / inspect (designator) | Étudier | `Core/Keyed/Designators.xml` (`DesignatorStudy`) |
| extract tree | Déraciner | `Core/Keyed/Designators.xml` (`DesignatorExtractTree`) |
| remove floor | Enlever le sol | `Core/Keyed/Designators.xml` (`DesignatorRemoveFloor`) |
| expand zone | Agrandir zone / Étendre… | `Core/Keyed/Designators.xml` (`DesignatorZoneExpand` **Agrandir zone**, `DesignatorAreaHomeExpand` **Étendre** la zone de base) |
| delete / shrink zone | Supprimer zone / Réduire | `Core/Keyed/Designators.xml` (`DesignatorZoneDeleteSingular` **Supprimer zone**, `DesignatorPlanShrinkSingular` **Réduire** le plan) |
| paint (building) | Peindre | `Core/Keyed/Designators.xml` (`DesignatorPaintBuilding` **Peindre** mur) |
| build roof | Étendre la zone de couverture | `Core/Keyed/Designators.xml` (`DesignatorAreaBuildRoofExpand`) |
| designate (verb, in a description) | désigner | `Core/Keyed/Designators.xml` (`DesignatorMineDesc` **Désigne** les zones de roche à miner.) |
| tend (a wound) | Soigner | `Core/Keyed/FloatMenu.xml` (`Tend` **Soigner {0}**) |
| rescue | Secourir | `Core/Keyed/FloatMenu.xml` (`Rescue` **Secourir {1_label}**) |
| arrest | Arrêter | `Core/Keyed/FloatMenu.xml` (`Arrest` **Arrêter {0}**) |
| capture | Capturer | `Core/Keyed/FloatMenu.xml` (`Capture` **Capturer {1_label}**) |
| equip | Équiper | `Core/Keyed/FloatMenu.xml` (`Equip` **Équiper {0}**) |
| attack | Attaquer | `Core/Keyed/FloatMenu.xml` (`Attack` **Attaquer {1_labelShort}**) |
| clean | Nettoyer | `Core/Keyed/FloatMenu.xml` (`CleanRoom` **Nettoyer {0_definite}**) |
| consume | Consommer | `Core/Keyed/FloatMenu.xml` (`ConsumeThing`) |
| call (on radio) | Appeler | `Core/Keyed/FloatMenu.xml` (`CallOnRadio`) |
| man (a turret) | Manipuler | `Core/Keyed/FloatMenu.xml` (`OrderManThing`) |
| remove | Supprimer | `Core/Keyed/FloatMenu.xml` (`Remove`) |
| visit | Visiter | `Core/Keyed/FloatMenu.xml` (`VisitSettlement` **Visiter {0}**) |
| trade with | Commercer avec / Échanger avec | `Core/Keyed/FloatMenu.xml` (`TradeWith` **Commercer avec {0}**); `Core/Keyed/FloatMenu.xml` (`TradeWithSettlement` **Échanger avec {0}**) |
| cannot … | Impossible de … | `Core/Keyed/FloatMenu.xml` (`CannotPickUp` **Impossible de** ramasser {1_labelShort}); `Biotech/Keyed/FloatMenu.xml` (`CannotControlMech` **Impossible de** contrôler {0}) |

### Buildings, zones, areas, build categories

| English | Français | Source file |
|---|---|---|
| growing zone | Zone agricole | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone`) |
| stockpile / storage zone | Zone de stockage | `Core/Keyed/Misc_Gameplay.xml` (`Stockpile`); `Core/Keyed/GameplayCommands.xml` (`CommandMakeBeaconStockpileLabel` Créer une **zone de stockage**) |
| dumping stockpile | décharge | `Core/Keyed/Designators.xml` (`DesignatorZoneCreateStorageDumpingDesc` Créer une **décharge**…) |
| home area | zone de base | `Core/Keyed/Designators.xml` (`DesignatorAreaHomeExpand` Étendre la **zone de base**) |
| allowed area | Zone autorisée | `Core/Keyed/MainTabs.xml` (`AllowedArea`); `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed`) |
| structure (build category) | structures | `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Structure.label`) |
| furniture | mobilier | same file (`Furniture.label`) |
| floors | sols | same file (`Floors.label`) |
| power | énergie | same file (`Power.label`) |
| production | production | same file (`Production.label`) |
| security | sécurité | same file (`Security.label`) |
| temperature (category) | température | same file (`Temperature.label`) |
| orders | ordres | same file (`Orders.label`) |
| recreation (category) | loisirs | same file (`Joy.label`) |
| misc | divers | same file (`Misc.label`) |
| ship | vaisseau | same file (`Ship.label`) |
| architect menu | menu Architecte | `Core/DefInjected/MainButtonDef/*` (`Architect.label` **architecte**); `Core/DefInjected/ConceptDef/Concepts_TriggeredModal.xml` (…du menu "**Architecte**") |
| storage priority (levels) | refusé / basse / normale / haute / importante / critique | `Core/Keyed/Enums.xml` (`StoragePriorityUnstored`…`StoragePriorityCritical`) |
| link storage settings | Paramètres de lien | `Core/Keyed/GameplayCommands.xml` (`LinkStorageSettings`) |

### Main tabs (the game's own tab names)

`Core/DefInjected/MainButtonDef/*` is the authority for every main-tab label:
`Animals.label` **animaux**, `Architect.label` **architecte**, `Assign.label` **assignations**,
`Factions.label` **factions**, `History.label` **historique**, `Inspect.label` **explorateur**,
`Menu.label` **menu**, `Quests.label` **quêtes**, `Research.label` **recherche**,
`Schedule.label` **planning**, `Wildlife.label` **faune**, `Work.label` **travail**,
`World.label` **monde**.

Inspect-tab names come from `Core/Keyed/ITabs.xml`: `TabHealth` **Santé**, `TabGear` **Matériel**,
`TabSocial` **Social**, `TabNeeds` **Besoins**, `TabTraining` **Entraîner**,
`TabPrisoner` **Prisonnier**, `TabBills` **Tâches**, `TabRecords` **Enregistrement**.

### Work, priorities, skills, needs

| English | Français | Source file |
|---|---|---|
| work (tab / noun) | travail | `Core/DefInjected/MainButtonDef/*` (`Work.label`) |
| bill (a work order) | tâche | `Core/Keyed/ITabs.xml` (`TabBills` **Tâches**); `Core/Keyed/ITabs.xml` (`AddBill` Ajouter une **tâche**). **NOT «facture».** |
| bill complete | Commande terminée | `Core/Keyed/Messages.xml` (`MessageBillComplete` **Commande terminée** : {0}) |
| priority | Priorité | `Core/Keyed/Misc_Gameplay.xml` (`Priority`) |
| manual priorities | Priorité manuelle | `Core/Keyed/MainTabs.xml` (`ManualPriorities`) |
| priority 0–4 | Ne fera pas / Top priorité / Haute priorité / Priorité normale / Basse priorité | `Core/Keyed/MainTabs.xml` (`Priority0`…`Priority4`) |
| disabled by backstory / trait / quest | Désactivé par l'origine / le trait / la quête | `Core/Keyed/MainTabs.xml` (`WorkDisabledByBackstory`, `WorkDisabledByTrait`, `WorkDisabledByQuest`) |
| skill level | Niveau de compétence | `Core/Keyed/Skills.xml` (`SkillLevel`) |
| level | Niveau | `Core/Keyed/Skills.xml` (`Level`) |
| experience | Expérience | `Core/Keyed/Skills.xml` (`Experience`) |
| passion | Passion | `Core/Keyed/Skills.xml` (`Passion`) |
| passion — none / minor / major | Pas de passion / Passion / Passion brûlante | `Core/Keyed/Skills.xml` (`PassionNone`, `PassionMinor`, `PassionMajor`) |
| trait | trait | `Core/Keyed/Skills.xml` (`TraitLabelWithDesc` **trait** {TRAITLABEL}) |
| backstory | origine | `Core/Keyed/MainTabs.xml` (`WorkDisabledByBackstory` Désactivé par l'**origine**) |
| thought | pensée | `Core/Keyed/Dialogs_Various.xml` (…auront les **pensées** suivantes) |
| memory (social) | souvenir | `Anomaly/Keyed/Letters.xml` (…les **souvenirs** de {TARGET_nameDef}) |
| mood | Humeur | `Core/Keyed/Misc_Gameplay.xml` (`Mood`); `Core/DefInjected/NeedDef/Needs.xml` (`Mood.label` **humeur**). **Distinct from «pensée» (thought).** |
| food (need) | nourriture | `Core/DefInjected/NeedDef/Needs.xml` (`Food.label`) |
| recreation / joy (need) | plaisir | same file (`Joy.label`) — note the *category* is «loisirs», the *need* is «plaisir» |
| rest | sommeil | same file (`Rest.label`) |
| comfort | confort | same file (`Comfort.label`) |
| beauty | beauté | same file (`Beauty.label`) |
| indoors / outdoors | intérieur / extérieur | same file (`Indoors.label`, `Outdoors.label`) |
| room size | espace | same file (`RoomSize.label`) |
| chemical (need) | chimique | same file (`DrugDesire.label`) |
| apparel | Vêtements | `Core/Keyed/Misc_Gameplay.xml` (`Apparel`) |
| forced apparel | Habillement forcé | `Core/Keyed/MainTabs.xml` (`ForcedApparel`) |
| policy (apparel/drug/food/reading) | règles | `Core/Keyed/MainTabs.xml` (`ManageApparelPolicies` Gérer **règles** vestimentaires, `ManageDrugPolicies` Gérer **règles** stupéfiants, `ManageFoodPolicies` Gérer **règles** alimentaires, `ManageReadingPolicies` Gérer **règles** de lecture) |

**Skill names** — `Core/DefInjected/SkillDef/Skills.xml`: `Animals` animaux, `Artistic` artiste,
`Construction` construction, `Cooking` cuisine, `Crafting` artisanat, `Intellectual` intellectuel,
`Medicine` médecine, `Melee` mêlée, `Mining` minage, `Plants` agriculture, `Shooting` tir,
`Social` sociabilité.

**Work types** — `Core/DefInjected/WorkTypeDef/WorkTypes.xml` gives `labelShort` / `pawnLabel` /
`verb` for each. Use the game's own value, never a re-translation:
art / Artiste / Sculpter · manutention / Manutentionnaire / Manutentionner ·
nettoyeur / Nettoyeur / Nettoyer · constructeur / Constructeur / Construire ·
cuisinier / Cuisinier / Cuisiner · artisan / Artisan / Fabriquer · médecin / Médecin / Soigner ·
pompier / Pompier / Éteindre le feu · fermier / Fermier / Cultiver · dresseur / Dresseur / Dresser ·
**transporteur / Transporteur / Transporter** (hauling) · chasseur / Chasseur / Chasser ·
mineur / Mineur / Miner · patient / Patient / Se faire soigner · repos / Patient / Se mettre au repos ·
forestier / Forestier / Couper des plantes · chercheur / Chercheur / Rechercher ·
forgeron / Forgeron / Forger · couturier / Couturier / Coudre ·
**geôlier / Geôlier / Garder** (warden).

### Health

| English | Français | Source file |
|---|---|---|
| health | Santé | `Core/Keyed/Menus_Main.xml` (`Health`); `Core/Keyed/ITabs.xml` (`TabHealth`) |
| overview | Vue d'ensemble | `Core/Keyed/ITabs.xml` (`HealthOverview`) |
| bleeding (rate) | Niveau d'hémorragie | `Core/Keyed/ITabs.xml` (`BleedingRate`) |
| tend (verb) | soigner | `Core/Keyed/FloatMenu.xml` (`Tend` **Soigner** {0}) |
| tend quality | Qualité du traitement | `Core/Keyed/Misc_Gameplay.xml` (`TendQuality`) |
| self-tend | auto-soin | `Core/Keyed/FloatMenu.xml` (`SelfTendDisabled` **auto-soin** désactivé) |
| medical care | soins médicaux | `Core/Keyed/FloatMenu.xml` (`MedicalCareDisabled` **soins médicaux** désactivés) |
| medical care levels | aucun soutien médical / visite du médecin sans médicaments / herbes médicinales ou pire / médicaments ou pire / meilleure qualité médicale | `Core/Keyed/Enums.xml` (`MedicalCareCategory_NoCare`…`MedicalCareCategory_Best`) |
| medical (bed) | Médicaliser | `Core/Keyed/GameplayCommands.xml` (`CommandBedSetAsMedicalLabel`) |
| operations (tab) | Opérations | `Core/Keyed/ITabs.xml` (`MedicalOperationsShort` Opérations) |
| pain | Douleur | `Core/Keyed/Menus_Main.xml` (`Pain`) |
| pain levels | qui démange / douloureuse / très douloureuse | `Core/Keyed/Enums.xml` (`PainCategory_LowPain`…`PainCategory_HighPain`) |
| hunger levels | Affamé / Faim urgente / Faim / Nourri | `Core/Keyed/Enums.xml` (`HungerLevel_Starving`…`HungerLevel_Fed`) |
| rest levels | Épuisé / Très fatigué / Fatigué / Reposé | `Core/Keyed/Enums.xml` (`HungerLevel_Exhausted`…`HungerLevel_Rested`) |
| hediff / body part | use the specific `HediffDef` / `BodyPartDef` label | `*/DefInjected/HediffDef/*`, `Core/DefInjected/BodyPartDef/*` |

### Trade

| English | Français | Source file |
|---|---|---|
| trade (verb) | Commercer | `Core/Keyed/Incidents.xml` (`CaravanMeeting_Trade`); `Core/Keyed/FloatMenu.xml` (`TradeWith` **Commercer avec {0}**) |
| trader | commerçant | `Core/Keyed/FloatMenu.xml` (`TraderDismissed` **Commerçant** renvoyé) |
| silver | argent | `Core/Keyed/Dialogs_Various.xml` (`YourTradeableSilver` **Argent** échangeable : {0}) |
| price — very cheap … exorbitant | très bon marché / bon marché / normal / cher / exorbitant | `Core/Keyed/Enums.xml` (`PriceTypeVeryCheap`…`PriceTypeExorbitant`) |
| negotiator | Négociateur | `Core/Keyed/Dialogs_Various.xml` (`NegotiatorTradeDialogInfo`) |
| mass carried | Charge transportée | `Core/Keyed/ITabs.xml` (`MassCarried` **Charge transportée** : {0} / {1} kg) |
| market value | use the game's `StatDef` label | `Core/DefInjected/StatDef/*` |

### Research

| English | Français | Source file |
|---|---|---|
| research | Recherche | `Core/Keyed/MainTabs.xml` (`Research`) |
| research progress | Progression de la recherche | `Core/Keyed/Misc_Gameplay.xml` (`ResearchProgress`) |
| research finished | Recherche terminée | `Core/Keyed/Dialogs_Various.xml` (`ResearchFinished` **Recherche terminée** : {0}) |
| techprint | schéma technique | `Core/Keyed/Misc.xml` (`TechprintLabel` **schéma technique** ({PROJECT_label})); `Core/Keyed/MainTabs.xml` (`ResearchTechprintRequirement` **Schémas techniques** requis {0} / {1}) |
| tech level | niveau technique | `Core/Keyed/Dialogs_Various.xml` (`TechLevelTooLow` Le **niveau technique** de votre faction est {0}…) |
| tech levels | animal / néolithique / médiéval / industriel / spatial / ultra / archotech | `Core/Keyed/Enums.xml` (`TechLevel_Animal`…`TechLevel_Archotech`) |

### Prisoner, slave, ideology, ritual, abilities

| English | Français | Source file |
|---|---|---|
| prisoner | Prisonnier / prisonnier / prisonniers | `Core/Keyed/Misc_Gameplay.xml` (`Prisoner`, `PrisonerLower`, `PrisonersLower`) |
| for prisoners (bed) | Pour les prisonniers | `Core/Keyed/GameplayCommands.xml` (`CommandBedSetForPrisonersLabel`) |
| recruitment resistance | Résistance | `Core/Keyed/ITabs.xml` (`RecruitmentResistance`) |
| recruitment difficulty | Difficulté de recrutement | `Core/Keyed/Dialogs_Various.xml` (`RecruitDifficulty`) |
| warden (work type) | geôlier | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.labelShort` geôlier, `Warden.pawnLabel` Geôlier) |
| slave | Esclave | `Core/Keyed/ITabs.xml` (`Slave`) |
| enslave | asservir | `Biotech/Keyed/Letters.xml` (`Enslave`) |
| suppression | Répression | `Ideology/Keyed/ITabs.xml` (`Suppression`) |
| ideoligion | idéoligion | `Ideology/Keyed/Misc_Gameplay.xml` (`Ideo` **idéoligion**) |
| precept | précepte / Préceptes | `Ideology/Keyed/Misc_Gameplay.xml` (`Precept`); `Ideology/Keyed/MainTabs.xml` (`Precepts`) |
| meme | Mèmes | `Ideology/Keyed/MainTabs.xml` (`Memes`) |
| ritual | Rituels / rituel | `Ideology/Keyed/MainTabs.xml` (`Rituals`); `Anomaly/Keyed/Misc_PsychicRituals.xml` (`PsychicRitual` **rituel psychique**) |
| role | rôle | `Ideology/Keyed/Dialogs_Various.xml` (`Role`) |
| certainty | certitude | `Ideology/Keyed/Misc_Gameplay.xml` (`Certainty`) |
| ability (psycast) | capacité | `Royalty/Keyed/GameplayCommands.xml` (`CommandPsycastPawnIsUnconscious` L'utilisateur de la **capacité** est inconscient.) |
| psyfocus | Concentration psychique | `Royalty/Keyed/Misc_Gameplay.xml` (`Psyfocus`); gizmo short form **Concentr.** (`PsyfocusLabelGizmo`) |
| psylink | lien psychique | `Royalty/Keyed/Dialogs_Various.xml` (`PsylinkTooLowForGainAbility` Le **lien psychique** de {PAWN_labelShort}…) |
| psychic | psychique | `Royalty/Keyed/Dialogs_Various.xml` (`AbilityTargetPsychicallyDeaf` La cible est **psychiquement** hermétique.) |
| neural heat / entropy | échauffement neuronal | `Royalty/Keyed/GameplayCommands.xml` (`CommandPsycastWouldExceedEntropy` L'**échauffement neuronal** excéderait la limite.) |
| psychic drone levels | aucun / faible / moyen / fort / extrême | `Core/Keyed/Enums.xml` (`PsychicDroneLevel_*`) |

### Biotech and Anomaly

| English | Français | Source file |
|---|---|---|
| gene | gène / gènes | `Core/Keyed/Skills.xml` (`GeneLabelWithDesc` **gène** {GENE_label}); `Biotech/Keyed/Misc_Gameplay.xml` (`Genes`) |
| genepack | pack de gènes | `Biotech/Keyed/Dialogs_Various.xml` (`SelectedGenepacks` **Packs de gènes** sélectionnés) |
| xenotype / xenogerm | Xénotype / Xénogerme | `Biotech/Keyed/Misc_Gameplay.xml` (`Xenotype`, `Xenogerm`) |
| metabolic efficiency | efficacité métabolique | `Biotech/Keyed/Misc_Gameplay.xml` (`Metabolism`) |
| mech (mechanoid) | mécanoïde | `Biotech/Keyed/Alerts.xml` (`AlertMechLacksOverseerDesc` …vos **mécanoïdes**…). Short colloquial **mech** also appears (`AlertMechLacksOverseer` **mechs** incontrôlés) |
| bandwidth | Bande conso | `Biotech/Keyed/Misc_Gameplay.xml` (`Bandwidth`) |
| overseer | superviseur | `Biotech/Keyed/GameplayCommands.xml` (`CommandSelectOverseerDisabledDesc` Pas de **superviseur**.) |
| entity | entité | `Anomaly/Keyed/Dialogs_Various.xml` (`EntitiesSection` **Entités**, `EntityCodex` Codex des **entités**) |
| study (Anomaly) | étude | `Anomaly/Keyed/Dialog_StatsReports.xml` (`StudyFrequency` Intervalle d'**étude**, `KnowledgeFromStudy` Connaissance obtenue suite à une **étude**) |
| holding platform | plateforme de détention | `Anomaly/Keyed/Misc_Gameplay.xml` (`NoHoldingPlatformsAvailable` Aucune **plateforme de détention** disponible.) |
| containment | confinement | `Anomaly/Keyed/FloatMenu.xml` (`NoEmptyEntityHolders` Aucun **confinement** vide) |

### Weather, season, time, dates

| English | Français | Source file |
|---|---|---|
| temperature | Température | `Core/Keyed/Misc_Gameplay.xml` (`Temperature`) |
| weather | ensoleillé / brouillard / brouillard pluvieux / pluie / tempête pluvieuse / orage sec / neige douce / neige intense / souterrains | `Core/DefInjected/WeatherDef/Weathers.xml` (`Clear.label`, `Fog.label`, `FoggyRain.label`, `Rain.label`, `RainyThunderstorm.label`, `DryThunderstorm.label`, `SnowGentle.label`, `SnowHard.label`, `Underground.label`) |
| season | printemps / été / automne / hiver | `Core/Keyed/Time.xml` (`SeasonSpring`…`SeasonWinter`) — all lowercase in the corpus |
| quadrum (the unit) | quadrum | `Core/Keyed/Dates.xml` (`DateReadoutTip` **Quadrum** actuel : {4}) |
| quadrum names | avrimai / juilloût / septobre / décemvier | `Core/Keyed/Time.xml` (`QuadrumAprimay`…`QuadrumDecembary`) |
| day(s) / hour(s) | jours / heures | `Core/Keyed/Time.xml` (`DaysLower`, `HoursLower`) — the corpus's generic forms are plural |
| letter abbreviations (d/h/m/s/y) | j / h / m / s / a | `Core/Keyed/Time.xml` (`LetterDay`, `LetterHour`, `LetterMinute`, `LetterSecond`, `LetterYear`) |
| clock time / date / year | Heure / Date / Année | `Core/Keyed/Dates.xml` (`ClockTime`, `ClockDate`, `ClockYear`) |
| "{0} ago" | il y a {0} | `Core/Keyed/Time.xml` (`TimeAgo`) |
| expires in {0} | Expire dans {0} | `Core/Keyed/MainTabs.xml` (`QuestExpiresIn`) |

### Combat, apparel, quality, stats

| English | Français | Source file |
|---|---|---|
| quality | Qualité | `Core/Keyed/Misc_Gameplay.xml` (`Quality`) |
| quality categories | horrible / médiocre / normal / bon / excellent / merveille / légendaire | `Core/Keyed/Enums.xml` (`QualityCategory_Awful`…`QualityCategory_Legendary`) |
| quality short forms | horr / mdcr / nrml / bon / exc / merv / legd | `Core/Keyed/Enums.xml` (`QualityCategoryShort_*`) |
| hit points | points de vie | `Core/Keyed/Misc_Gameplay.xml` (`HitPointsBasic`) |
| damage / accuracy / range | Dégâts / Précision / Portée | `Core/Keyed/Dialogs_Various.xml` (`Damage`, `Accuracy`, `Range`) |
| hostility response | Ignorer / Attaquer / Fuir | `Core/Keyed/Enums.xml` (`HostilityResponseMode_*`) |
| light level | Sombre / Éclairé / Bien éclairé | `Core/Keyed/Enums.xml` (`Dark`, `Lit`, `LitBrightly`) |
| compass directions (short) | N / NE / E / SE / S / SO / O / NO | `Core/Keyed/Enums.xml` (`Direction8Way_*_Short`) — note **SO / O / NO**, not SW / W / NW |

### Gizmo / command / button labels

| English | Français | Source file |
|---|---|---|
| draft | Mobiliser | `Core/Keyed/GameplayCommands.xml` (`CommandDraftLabel`) |
| undraft | Démobiliser | `Core/Keyed/GameplayCommands.xml` (`CommandUndraftLabel`) |
| drafted (state) | mobilisé / Pendant la mobilisation | `Core/Keyed/Enums.xml` (`ShowWeapons_WhileDrafted`, `MechNameDisplayMode_WhileDrafted` **Pendant la mobilisation**) |
| fire at will | Autoriser le tir | `Core/Keyed/GameplayCommands.xml` (`CommandFireAtWillLabel`) |
| hold fire | Cesser le feu | `Core/Keyed/GameplayCommands.xml` (`CommandHoldFire`) |
| toggle power | Activer l'alimentation électrique | `Core/Keyed/GameplayCommands.xml` (`CommandTogglePowerLabel`) |
| hold open (door) | Maintenir ouvert | `Core/Keyed/GameplayCommands.xml` (`CommandToggleDoorHoldOpen`) |
| forbid passage (door) | Verrouiller | `Core/Keyed/GameplayCommands.xml` (`CommandToggleDoorForbid`) |
| toggle study | Activer/Désactiver l'étude | `Core/Keyed/GameplayCommands.xml` (`CommandToggleStudy`) |
| view quest | Voir la quête : {0} | `Core/Keyed/GameplayCommands.xml` (`CommandViewQuest`) |
| copy / paste | Copier / Coller | `Core/Keyed/MainTabs.xml` (`Copy`, `Paste`) |
| rename | Renommer | `Core/Keyed/Misc_Gameplay.xml` (`Rename`) |
| edit | Modifier… | `Core/Keyed/MainTabs.xml` (`AssignTabEdit`) |
| accept quest | Accepter la quête | `Core/Keyed/MainTabs.xml` (`AcceptQuest`) |

### UI chrome

| English | Français | Source file |
|---|---|---|
| accept | Accepter | `Core/Keyed/Dialogs_Various.xml` (`AcceptButton`) |
| cancel | Annuler | `Core/Keyed/Dialogs_Various.xml` (`CancelButton`) |
| close | Fermer | `Core/Keyed/Dialogs_Various.xml` (`CloseButton`) |
| confirm | Confirmer | `Core/Keyed/Dialogs_Various.xml` (`Confirm`) |
| OK | OK | `Core/Keyed/Dialogs_Various.xml` (`OK`) |
| yes / no | Oui / Non | `Core/Keyed/Misc.xml` (`Yes`, `No`) |
| back / next | Retour / Suivant | `Core/Keyed/Menus_Main.xml` (`Back`, `Next`) |
| options | Options | `Core/Keyed/Menus_Main.xml` (`Options`) |
| menu | menu | `Core/DefInjected/MainButtonDef/*` (`Menu.label`) |
| enable / disable | Activer / Désactiver | `Core/Keyed/Menus_Main.xml` (`Enable`); `Core/Keyed/Dialogs_Various.xml` (`Disable`) |
| enabled / disabled (state) | Activé / Désactivé | `Core/Keyed/Menus_Main.xml` (`Enabled`, `Disabled`) |
| add / remove | ajouter / Supprimer | `Core/Keyed/Dialogs_Various.xml` (`Add` **ajouter**, lowercase in the corpus); `Core/Keyed/FloatMenu.xml` (`Remove`) |
| delete | Supprimer | `Core/Keyed/Menus_Main.xml` (`Delete`) |
| save / load | Sauver / Charger | `Core/Keyed/Menus_Main.xml` (`Save`, `Load`) |
| reset | Réinitialiser | `Core/Keyed/Dialogs_Various.xml` (`Reset`) |
| default | Par défaut / défaut | `Core/Keyed/Misc.xml` (`Default`, `default`) |
| none | Aucune / Aucun | `Core/Keyed/ITabs.xml` (`None` **Aucune**). **Agree with the noun it replaces** — see §3.3 |
| (none) bracketed | (Rien) | `Core/Keyed/MainTabs.xml` (`NoneBrackets`) |
| no … (absence) | Aucun / Aucune / Pas de … | `Biotech/Keyed/FloatMenu.xml` (`NoViablePawns` **Aucune** personne disponible); `Anomaly/Keyed/Misc_Gameplay.xml` (`MonolithActivateDisabledPawns` **Aucun** colon disponible.); `Biotech/Keyed/Dialogs_Various.xml` (`NoGenePacks` **Pas de** packs de gènes) |
| description | Description | `Core/Keyed/Misc_Gameplay.xml` (`Description`) |
| details | Détails | `Core/Keyed/Dialogs_Various.xml` (`Details`) |
| filter | Filtre | `Core/Keyed/Misc_Gameplay.xml` (`Filter`) |
| search (verb) | Rechercher | `Core/Keyed/Dialogs_Various.xml` (`SearchTheMap` **Rechercher** sur la carte actuelle., `SearchTheWorld` **Rechercher** dans le monde) |
| searching | Recherche en cours | `Core/Keyed/Dialogs_Various.xml` (`Searching`) |
| search results | {0} résultats trouvés | `Core/Keyed/Dialogs_Various.xml` (`MapSearchResults`) |
| collapse all | Réduire | `Core/Keyed/Dialogs_Various.xml` (`CollapseAllCategories`) |
| click | Cliquez / cliquer | `Core/Keyed/MainTabs.xml` (`ClickToJumpTo` **Cliquez** pour vous rendre à :) |
| press (a key) | Appuyez sur | `Core/Keyed/Menu_KeyBindings.xml` (`PressAnyKeyOrEsc` **Appuyez sur** n'importe quelle touche ou Échap…) |
| key | touche | `Core/Keyed/Menu_KeyBindings.xml` (`PressAnyKeyOrEsc`) |
| hotkey / key binding | Raccourci / Raccourci clavier | `Core/Keyed/Misc_Gameplay.xml` (`HotKeyTip` **Raccourci**); `Core/Keyed/Menu_KeyBindings.xml` (`KeyBindingOverwritten` **Raccourci clavier** remplacé : {0}) |
| tab (UI tab) | onglet | `Core/Keyed/GameplayCommands.xml` (…dans l'**onglet** Quêtes.); `Core/Keyed/Alerts.xml` (l'**onglet** OBJETS) |
| button | bouton | `Core/Keyed/Menu_KeyBindings.xml` (`PressAnyKeyOrEscController` …n'importe quel **bouton**…); `Core/Keyed/Alerts.xml` (le **bouton** 'i') |
| cursor | curseur | `Core/Keyed/Menu_Options.xml` (`ZoomToMouse` Zoom sur le **curseur** de la souris) |
| scroll / scrolling | Défilement / défiler | `Core/Keyed/Menu_Options.xml` (`EdgeScreenScroll` **Défilement** sur les bords d'écran, `MapDragSensitivity` **Défilement** carte: sensibilité) |
| drag | glisser | `Core/Keyed/Misc_Gameplay.xml` (`ShowColonistBarToggleButton` …avec un clic droit, puis **glisser**) |
| UI (interface) | interface | `Core/Keyed/Menu_Options.xml` |
| inventory | Inventaire | `Core/Keyed/Misc_Gameplay.xml` (`Inventory`) |
| beauty / wealth etc. | use the game's `StatDef` / `NeedDef` label | `*/DefInjected/StatDef/*` |

---

## Section 2 — Accessibility-specific vocabulary (mod-coined)

These concepts RimWorld does NOT name. We lock a French rendering here. Where the game's own UI
offers a match, we reuse it and cite it so users hear familiar wording; otherwise we follow standard
French screen-reader convention (NVDA-fr, JAWS-fr, VoiceOver-fr).

| English | Français (locked) | Rationale / source |
|---|---|---|
| screen reader | lecteur d'écran | Standard French term (NVDA-fr, JAWS-fr). Universally understood |
| announce (verb) | annoncer | Established French TTS verb. Prefer over «énoncer» (too literary) and «dire» (too vague) |
| announcement (noun) | annonce | Matches the verb above |
| navigate / navigation | naviguer / navigation | Standard French UI term; NVDA-fr uses «navigation» |
| cursor | curseur | RimWorld uses it: `Core/Keyed/Menu_Options.xml` (`ZoomToMouse` Zoom sur le **curseur** de la souris) |
| map scanner | scanner de carte | Mod-specific feature. «scanner» is the accepted French computing loanword and unambiguous in TTS. The game itself uses «Balayage» for orbital scanning (`Odyssey/Keyed/Misc_Gameplay.xml` `OrbitalScannerScanning`) — do NOT reuse that for our scanner |
| search (the act) | recherche | RimWorld's noun: `Core/Keyed/Dialogs_Various.xml` (`Searching` **Recherche** en cours) |
| typeahead search | recherche par saisie | No fixed game term. Transparent in TTS; «saisie» is the standard French word for typed input |
| tree view | arborescence | Standard French UI term; NVDA-fr announces tree controls as «arborescence» |
| node (tree node) | nœud | Standard French UI term. Note the ligature **œ** (U+0153) |
| level (tree depth) | niveau | RimWorld uses it: `Core/Keyed/Skills.xml` (`Level` Niveau). e.g. «niveau {0}» |
| expand | Étendre / Développer | RimWorld's own: `Core/Keyed/Designators.xml` (`DesignatorExpandPlan` **Étendre** le plan). Use **Étendre** for the verb on a command label; **Développer** is the NVDA-fr term and is acceptable inside a state phrase |
| collapse | Réduire | RimWorld's own counterpart: `Core/Keyed/Dialogs_Various.xml` (`CollapseAllCategories` **Réduire**); `Core/Keyed/Designators.xml` (`DesignatorPlanShrinkSingular` **Réduire** le plan) |
| expanded (state) | développé | Standard NVDA-fr state word. Masculine singular; never agree it with the item — see §3.3 |
| collapsed (state) | réduit | Standard NVDA-fr state word. Masculine singular |
| toggle (verb) | Activer/Désactiver | RimWorld's own toggle wording: `Core/Keyed/GameplayCommands.xml` (`CommandToggleStudy` **Activer/Désactiver** l'étude); `Anomaly/Keyed/Misc_Gameplay.xml` (`BioferriteHarvesterToggleUnloading`). Keep the slash, no spaces around it. «Basculer» also appears in the corpus (`Biotech/Keyed/GameplayCommands.xml`) and is acceptable where the slash form would be clumsy |
| on / off (state) | activé / désactivé | `Core/Keyed/Menus_Main.xml` (`Enabled`, `Disabled`) |
| checkbox | case à cocher | Standard French UI term (NVDA-fr). Consistent with the game's «case» for a map cell but unambiguous in context |
| checked / not checked | coché / non coché | Standard; pairs with «case à cocher» |
| partially checked | partiellement coché | Standard NVDA-fr wording |
| slider | curseur / glissière | NVDA-fr announces «curseur». Because the game already uses «curseur» for the mouse pointer, prefer **glissière** when the two could collide in the same announcement; otherwise **curseur** is fine and more familiar |
| spin box / stepper | zone de sélection numérique | Standard NVDA-fr term for a numeric +/- control. For the bounds reuse **minimum** / **maximum** |
| combo box | zone de liste déroulante | Standard NVDA-fr term |
| radio button | bouton radio | Standard French UI term |
| edit box / text field | zone d'édition | Standard NVDA-fr term |
| button (UI) | bouton | RimWorld uses it: `Core/Keyed/Alerts.xml` (…accessible avec le **bouton** 'i'.) |
| tab (UI tab) | onglet | RimWorld uses it throughout: `Core/Keyed/GameplayCommands.xml` (l'**onglet** Quêtes) |
| selected | sélectionné | RimWorld uses it: `Biotech/Keyed/Misc_Gameplay.xml` (`PersonSelected` Personne **sélectionnée**). In OUR strings keep it masculine singular and invariable — see §3.3 |
| not selected | non sélectionné | Negation of the above, masculine singular |
| read-only | lecture seule | Standard French computing term |
| focused | ciblé | «ciblé» is the plainest French for UI focus and avoids collision with the game's «Concentration psychique» (`Royalty/Keyed/Misc_Gameplay.xml` `Psyfocus`) |
| row / column | ligne / colonne | Standard French UI terms |
| column header | en-tête de colonne | Standard French UI term |
| cell (table cell) | cellule | Standard French UI term. NOTE: a *map* cell is «case» (`Core/Keyed/Misc_Gameplay.xml` `SelectNextInSquareTip`) — keep the two distinct |
| table | tableau | Standard French UI term |
| list | liste | Standard |
| sortable / sorted ascending / descending | triable / tri croissant / tri décroissant | Standard French UI terms |
| blank (empty cell) | vide | Standard |
| position ("X of Y") | {0} sur {1} | Standard French UI phrasing. RimWorld's own count-of-total shape is the bare ratio «{0} / {1}» (`Core/Keyed/MainTabs.xml` `ResearchTechprintRequirement`) — use the bare ratio only where the surrounding phrase already makes it unambiguous in speech |
| jump to | Aller à : {0} | Built from RimWorld's `Core/Keyed/MainTabs.xml` (`ClickToJumpTo` Cliquez pour vous **rendre à** :) in the colon shape of §3.3 |
| edge / boundary | Déjà en haut / Déjà en bas | Plain French edge wording |
| hotkey | Raccourci | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`HotKeyTip` **Raccourci**) |
| accessibility | accessibilité | Standard French term |
| inspect / inspection | Étudier / explorateur | `Core/Keyed/Designators.xml` (`DesignatorStudy` **Étudier**); the inspect main tab is `Inspect.label` **explorateur** (`Core/DefInjected/MainButtonDef/*`) |

---

## Section 3 — Style rules for translators

### 3.1 Register — formal «vous», infinitive commands

RimWorld's French addresses the player as **«vous»**, throughout. It is not a stylistic choice we can
revisit; matching it is what makes the mod sound like the game.

Evidence (counted over the whole extraction):

- «vous» — **1453** occurrences; «votre» / «vos» — **756**. e.g. «Le niveau technique de **votre**
  faction est {0}» (`Core/Keyed/Dialogs_Various.xml` `TechLevelTooLow`), «Argent échangeable»
  addressed to the player (`YourTradeableSilver`), «Jours depuis **votre** arrivée»
  (`Core/Keyed/Dates.xml` `DateReadoutTip`)
- «tu» / «ton» / «tes» — the 153 apparent hits are all false positives («tué», the note name «ton»).
  The corpus never addresses the player informally.
- Formal imperative when instructing: «**Cliquez** pour vous rendre à :» (`Core/Keyed/MainTabs.xml`
  `ClickToJumpTo`), «**Appuyez sur** n'importe quelle touche ou Échap»
  (`Core/Keyed/Menu_KeyBindings.xml` `PressAnyKeyOrEsc`), «**Faites** votre choix parmi les divers
  revêtements de sol» (`Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml`
  `Floors.description`)

**Never use «tu» or the informal imperative («clique», «appuie»).**

**Command labels are INFINITIVES**, not imperatives and not nouns: «Annuler», «Miner»,
«Transporter», «Chasser», «Interdire», «Fermer», «Sauver», «Mobiliser», «Renommer»
(`Core/Keyed/Designators.xml`, `Core/Keyed/Menus_Main.xml`, `Core/Keyed/GameplayCommands.xml`). Do
not write «Annulez» or «Annulation» for a button.

**Descriptions are impersonal third person or a «vous» imperative**: «**Désigne** les zones de roche
à miner.» (`DesignatorMineDesc`), «**Affiche** ou non la beauté du paysage…»
(`Core/Keyed/Misc_Gameplay.xml` `ShowBeautyToggleButton`), «**Créer** une zone de stockage dans
laquelle les colons déposeront…» (`DesignatorZoneCreateStorageResourcesDesc`).

**States are nouns or masculine past participles**: «Humeur», «Douleur», «Activé», «Désactivé»,
«développé», «réduit», «sélectionné». Otherwise match RimWorld's neutral, terse UI register. No
exclamation marks unless the English has them. No first person. No emoji.

### 3.2 The French language worker — what it fixes for you, and what it does not

French is one of the few languages with a real `LanguageWorker`, and it changes what you are allowed
to write. `Verse/LanguageWorker_French.PostProcessed` applies five regex rewrites to the **finished,
placeholder-substituted** string:

| Rewrite | Effect |
|---|---|
| `\b([cdjlmnst]\|qu\|quoiqu\|lorsqu)e ` + vowel/h | `de atelier` → `d'atelier`, `le établi` → `l'établi`, `que il` → `qu'il` |
| `\bla ` + vowel/h | `la armure` → `l'armure` |
| `\bsi (ils?)\b` | `si il` → `s'il`, `si ils` → `s'ils` |
| `\bde l(es?)\b` | `de le` → `du`, `de les` → `des` |
| `\bà les?\b` | `à le` → `au`, `à les` → `aux` (case-preserving: `À le` → `Au`) |

**When it runs.** `PostProcessed` is called from `GrammarResolverSimple.Formatted` (line 79,
unconditionally). Our mod reaches that path through `Localized.Loc(key, args)` →
`key.Translate(args)` → `.Formatted(args)` — so **every key we pass arguments to gets
post-processed**. A key with no arguments (`"Key".Translate()` / `.Loc()` with no args) does **not**.

**What this licenses.** Write the article-plus-placeholder composition in its uncontracted form and
let the worker fix it at runtime. This is exactly what the official corpus does:

- «Cliquez pour voir les gènes **de {PAWN_nameDef}**.» (`Biotech/Keyed/ITabs.xml` `ViewGenesDesc`) —
  becomes «…de Anna» → «…d'Anna»
- `de {placeholder}` appears **754×** in the corpus; `d'{placeholder}` appears **0×**. Never write the
  elided form yourself — you cannot know whether the runtime value starts with a vowel.
- `le {0}` (26×) / `la {1}` (20×) are written in full; `l'{0}` appears once in the entire corpus.

**What it does NOT do.** It does not choose gender, it does not pluralize, and it does not repair a
sentence that was ungrammatical before substitution. Write fully correct French yourself; treat the
worker purely as the fix for compositions that cross a placeholder boundary.

### 3.3 Gender — the critical French decision

Our keys carry **no gender information**. Verified: `Languages/English/Keyed/` contains **zero**
grammar tags, every placeholder is a plain `{0}` / `{named}` `string.Format` argument, and every
`.Named("PAWN")` call in `src/` targets a **vanilla** key, never a `RimWorldAccess.*` key. So the
word that lands in `{0}` is an already-resolved label string whose gender we cannot know at
translation time.

**Therefore: never write an adjective, participle, or article that must agree with a placeholder.**

The official corpus solves this with **gender-conditional tags** — `{PAWN_gender ? é : ée : é(e)}`
and friends, **1984** occurrences (`{PAWN_gender ? é : ée : é(e)}` alone 628×). Those work because
vanilla passes live `Pawn` objects. **We cannot use them**, with one narrow exception documented
below. Do not copy them into our files.

**Use these strategies instead, in this order of preference.**

**Strategy A — the noun-or-participle + « : » shape. This is the workhorse; prefer it.**

Put a French head word you control in front of the colon and let the placeholder stay bare. The
agreement then lands on *your* word, whose gender you know. The corpus does this constantly:

| Corpus value | Key / file |
|---|---|
| `Argent échangeable : {0}` | `YourTradeableSilver`, `Core/Keyed/Dialogs_Various.xml` |
| `Recherche terminée : {0}` | `ResearchFinished`, `Core/Keyed/Dialogs_Various.xml` |
| `Commande terminée : {0}` | `MessageBillComplete`, `Core/Keyed/Messages.xml` |
| `Objet produit : {0}` | `MessageCompSpawnerSpawnedItem`, `Core/Keyed/Messages.xml` |
| `Désactivé par le trait : {0}` | `WorkDisabledByTrait`, `Core/Keyed/MainTabs.xml` |
| `Compétence liée : {0}` | `RelevantSkills`, `Core/Keyed/MainTabs.xml` |
| `Voir la quête : {0}` | `CommandViewQuest`, `Core/Keyed/GameplayCommands.xml` |
| `Charge transportée : {0} / {1} kg` | `MassCarried`, `Core/Keyed/ITabs.xml` |
| `Alerte critique : {0}` | `MessageCriticalAlert`, `Core/Keyed/Messages.xml` |
| `Ne peut pas équiper : {1}` | `MessageCantEquipIncapableOfViolence`, `Core/Keyed/Messages.xml` |

So English `{0} selected` becomes **`Sélection : {0}`** (a noun — fully gender-free), not
«{0} sélectionné(e)». English `Activated {0}` becomes **`Activation : {0}`** or
**`Activé : {0}`**, never «{0} activé».

**Strategy B — an infinitive or a preposition, which never agrees.**

Verbs in the infinitive and prepositions carry no gender, so the placeholder stays bare:
«**Attaquer** {1_labelShort}», «**Soigner** {0}», «**Commercer avec** {0}»,
«**Impossible de** ramasser {1_labelShort}» (`Core/Keyed/FloatMenu.xml`),
«**Impossible de** contrôler {0}» (`Biotech/Keyed/FloatMenu.xml` `CannotControlMech`). `à {placeholder}` appears
128× and `de {placeholder}` 754×; both are safe (the worker contracts them where needed, §3.2).

**Strategy C — an appositive head noun that carries the agreement.**

Put a generic noun before or after the placeholder and agree with *that*. Useful head nouns for our
strings: **élément** (m., a list item), **objet** (m., an item), **ligne** (f., a row),
**colonne** (f.), **onglet** (m., a tab), **bouton** (m.), **zone** (f.), **case** (f., a map cell),
**personne** (f.) / **colon** (m.) for a pawn, **rituel** (m.), **gène** (m.), **trait** (m.). The
corpus's own examples: «**trait** {TRAITLABEL}» (`Core/Keyed/Skills.xml` `TraitLabelWithDesc`),
«**gène** {GENE_label}» (`GeneLabelWithDesc`), «**schéma technique** ({PROJECT_label})»
(`Core/Keyed/Misc.xml` `TechprintLabel`).

**Strategy D — masculine singular, invariable, for a fixed state word.**

Where the state word stands alone as its own key (our `RimWorldAccess.Shell.State.*` set:
`selected`, `expanded`, `read only`…), write the **masculine singular** and never vary it:
«sélectionné», «développé», «réduit», «coché», «non coché», «désactivé», «lecture seule». A screen
reader speaks these as fixed role/state tokens, exactly as NVDA-fr does; a `(e)` in the middle of a
spoken phrase is read aloud as «parenthèse e parenthèse» and is strictly worse. **Never write
`sélectionné(e)`, `activé(e)`, or any `(e)` / `(se)` / `(le)` parenthetical.** The corpus's own
`(s)`/`(e)` forms are 3 untranslated leftovers, not a convention.

**«Aucun» vs «Aucune».** These *do* agree, and you control the noun that follows, so agree normally:
«**Aucun** colon disponible.» (`Anomaly/Keyed/Misc_Gameplay.xml` `MonolithActivateDisabledPawns`),
«**Aucune** personne disponible» (`Biotech/Keyed/FloatMenu.xml` `NoViablePawns`), «**Aucune** plateforme
de détention disponible.» (`Anomaly/Keyed/Misc_Gameplay.xml` `NoHoldingPlatformsAvailable`),
«**Pas de** packs de gènes» (`Biotech/Keyed/Dialogs_Various.xml` `NoGenePacks`). When
the missing thing is a bare placeholder, restructure: «Aucun résultat pour '{0}'» rather than trying
to agree with `{0}`.

**The narrow exception (document-only; do not use).** `GrammarResolverSimple.TryResolveSymbol`
(line 965) does accept `{0_definite}`, `{0_indefinite}`, `{0_plural}` and `{0_gender ? … }` on a
**plain string** argument, resolving gender through `LoadedLanguage.ResolveGender` — which consults
Core's French `WordInfo/Gender/Male.txt` and `Female.txt` word lists and **falls back to
`Gender.Male`** for anything absent. Our arguments are pawn names, composed labels, mod def labels
and numbers, almost none of which are in those lists, so these tags would silently produce masculine
French for feminine nouns. You will see `{0_definite}` in the corpus (`Core/Keyed/FloatMenu.xml`
`CleanRoom` «Nettoyer {0_definite}») — that is vanilla passing a known `Thing`. **Do not add these
tags to our files.**

### 3.4 Plurals — flat plural after a count; `_numCase` is FORBIDDEN

**Finding: the French corpus contains ZERO `_numCase` usages.** Verified over the whole extraction
(Core + all five DLCs, `Keyed/` and `DefInjected/`):

```
$ grep -rho '_numCase' <fr_ref>/ | wc -l
0
```

**So the mod must never use `_numCase` in French.** `GrammarResolverSimple.ResolveNumCase` checks the
branch count against
`LanguageDatabase.activeLanguage.info.totalNumCaseCount ?? activeLanguageWorker.TotalNumCaseCount`
(line 1231). `LanguageWorker_French` does **not** override `TotalNumCaseCount`, so it inherits
`LanguageWorker`'s `0`, and Core's French `LanguageInfo.xml` leaves `totalNumCaseCount` unset. A
multi-branch `_numCase` tag fails the count check, logs an error, and **returns an empty string** — a
silently blank spoken announcement. We cannot override this from a mod: `LoadedLanguage.LoadMetadata`
takes the **first** `LanguageInfo.xml` across `RunningMods` and returns, and mods are forced to load
after Core, so Core's French metadata always wins.

**French does not need it.** French has exactly two number forms, and the corpus writes the plural
flat after a count placeholder:

- «{0} **jours**» — 28×, plus «{MOODDAYS} jours» 13×, «{1} jours» 5× (e.g.
  `Core/Keyed/Dates.xml` `DateReadoutTip`)
- «{0} **colons**», «{NUMCULPRITS} colons»
- «{0} **heures**» 3×, «{0} **objets**», «{1} objets», «{0} **éléments**»
- «{0} **résultats** trouvés» (`Core/Keyed/Dialogs_Various.xml` `MapSearchResults`)
- «{0} **cases**» 4×, «{0} **fois**» 4×, «{0} **animaux**» 2×, «{0} **personnes**»

Parenthetical «(s)» after a count placeholder appears **3×** in the whole corpus, and every instance
is an untranslated English leftover («{0} spectator(s)», «{0} patient(s)», «{1} partenaire(s)»).
**Never write `(s)`** — TTS reads the parentheses aloud.

#### One / Many key PAIRS

Many of our keys come in a `…One` / `…Many` pair, chosen in C# by `count == 1`. **French should use
both forms properly** — this is a real advantage over languages that cannot distinguish:

| Key | French |
|---|---|
| `…One` (count == 1) | singular: `{0} élément`, `{0} jour`, `{0} colon`, `{0} résultat` |
| `…Many` (count != 1) | plural: `{0} éléments`, `{0} jours`, `{0} colons`, `{0} résultats` |

Watch the agreement of any surrounding words: `Expanded {0} item` → `{0} élément développé`, and
`Expanded {0} items` → `{0} éléments développés`.

Note that `count == 0` selects `…Many`, so French will say «0 éléments». Strict French prefers
«0 élément», but the corpus does not distinguish either, and the plural reads naturally in speech.
Do not try to work around it. Always fill **both** keys — leaving one blank makes the mod fall back
to English for that count.

### 3.5 Punctuation, typography, numbers

**French spacing before `? ! : ;` — plain ASCII space, and only inside real prose.**

The corpus does apply French typographic spacing, and it uses an **ordinary ASCII space** to do it:
4537 plain-space-before-colon versus 128 no-break spaces (U+00A0), 2155 plain-space-before-`?` versus
12, 409 before `!`, 23 before `;`. There are 8 narrow no-break spaces (U+202F) in the entire
extraction.

So:

1. **Never emit U+00A0 or U+202F.** Plain `U+0020` only. A no-break space can reach the TTS bridge as
   an unexpected byte sequence and is invisible in review.
2. **Inside a genuine French sentence or a label-plus-value phrase you wrote, put one ASCII space
   before `:`, `?`, `!`, `;`** — matching the corpus: «Argent échangeable : {0}», «Recherche
   terminée : {0}», «Voulez-vous malgré tout les appeler à l'aide ?»
   (`Core/Keyed/Dialog_Trees.xml`). (The corpus is not perfectly consistent — «Désactivé par
   l'origine: {0}» in `Core/Keyed/MainTabs.xml` omits it — but the spaced form is the overwhelming
   majority. Follow the majority.)
3. **OVERRIDE — fragment joiners stay byte-identical to English.** Many of our keys are pure glue
   templates whose entire value is placeholders plus punctuation: `{0}. {1}`, `{0}: {1}`, `, {0}`,
   `{0} - {1}`, `{0}. {1}. No matches for '{2}'`. That punctuation is a **TTS pause boundary between
   two independently-spoken fragments**, not French sentence punctuation. Copy it exactly: the ASCII
   `.` / `,` / `:` / `-`, no space added before it, exactly one ASCII space after it. Do **not**
   rewrite `{0}: {1}` as `{0} : {1}`.

   The test: is the text immediately left of the punctuation **French words you translated**
   (→ rule 2, add the space) or a **placeholder / another announcement fragment** (→ rule 3, leave it
   alone)?

**Apostrophes: use the straight ASCII `'` (U+0027).** The corpus uses it 18482× against 2076 curly
`’` (U+2019). Straight is the majority and is what our English keys already contain (`'{0}'` quoting
around a search string).

**Quotation marks.** The corpus is mixed and sparse: 81 guillemets («…», sometimes spaced «
Prisonnier », sometimes not «chêne»), plus straight `'…'` and `"…"`. Our English keys quote an
interpolated value as `'{0}'` (e.g. `No matches for '{0}'`). **Keep the straight single quotes** —
they survive the TTS bridge unchanged and match the English shape. Do not introduce guillemets.

**Ellipsis.** The corpus uses `...` (268×) more than `…` (83×), but uses `…` on button labels
(`Core/Keyed/MainTabs.xml` `AssignTabEdit` «Modifier…»). Copy whatever the English source has; do not
convert between the two.

**Accents and ligatures are load-bearing.** Type «É», «è», «ê», «à», «ç», «û», «ô», «œ» directly
(«Étudier», «Réduire», «geôlier», «nœud», «Épuisé»). A capital letter keeps its accent in this corpus
— «**É**tendre», «**É**tudier», «**É**quiper», «**É**puisé», «**É**chap». Never strip an accent from
a capital, and never let a find-and-replace normalise these away.

**Capitalization: sentence case.** RimWorld's French capitalizes the first word only —
«Priorité manuelle», «Habillement forcé», «Gérer règles alimentaires», «Vue d'ensemble»,
«Zone de stockage», «Niveau de compétence». Do **not** use English Title Case. Many `DefInjected`
`*.label` values are deliberately all-lowercase because the game capitalizes them at render time
(`Furniture.label` «mobilier», `Food.label` «nourriture», `SeasonSpring` «printemps»). When you
translate a key whose English is lowercase, keep it lowercase.

**Numbers arrive pre-formatted through placeholders. Never reformat them.** French convention is a
comma decimal separator, but the game formats the value before it reaches the string — do not add,
remove, or convert separators inside `{0}`. **Percent signs:** do not add one; the corpus's own
handful of literal percents are all the postfix shape «{0}%». Keep whatever the English source has.
**Ordinals** are produced by `LanguageWorker_French.OrdinalNumber` as `1er` and `{N}e` (never
«ème»/«ième»); if you must write one literally, match that.

### 3.6 Placeholders, line breaks, keys, spacing

- Copy `{0}`, `{1}`, `{NamedArg}`, `{label}`, `{count}` etc. **byte-for-byte.** Never translate,
  never add or remove braces, never renumber. The **set** of placeholders in a value must match the
  English source exactly.
- You **may reposition** a placeholder for natural French word order — `string.Format` is positional.
  French is not verb-final, so this is needed less often than in Turkish or Russian, but the corpus
  does it freely (e.g. English `Upgrade {0} to level {1}` restructured around the colon shape).
  Repositioning is fine; renumbering or translating is forbidden.
- **Do not add grammar tags** (`_definite`, `_indefinite`, `_plural`, `_gender ? …`, `_numCase`) that
  the English source does not have. See §3.3 and §3.4 for why.
- Copy `\n` line breaks exactly and keep them in the same logical spots.
- **XML keys are never translated** — every element name stays byte-for-byte identical to English, or
  the string silently fails to load.
- **XML comments are developer context — leave them in English.** Do not translate `<!-- … -->`.
- **Preserve leading/trailing spaces in values.** Some keys are suffixes that join onto a preceding
  label (`RimWorldAccess.InfoCard.Inspectable` = `" Inspectable."` with a leading space;
  `RimWorldAccess.UI.FloatMenu.UnavailableSuffix` = `" (unavailable)"`). Keep the exact leading and
  trailing space.
- Keep a single normal space where a number or placeholder abuts French text: «{0} éléments»,
  «niveau {0}». Do not glue them together, and do not insert spaces around the `/` in
  «Activer/Désactiver».
- Escape XML specials as the English file does. A literal `&` must be `&amp;`, `<` must be `&lt;`.
  Accented French characters need no escaping — the files are UTF-8.

### 3.7 False friends and machine-translation traps

Machine translation gets these wrong in this game's domain. Every replacement below is the corpus's
own word.

| English | WRONG | RIGHT | Source |
|---|---|---|---|
| draft (a colonist) | brouillon, enrôler, recruter | **Mobiliser** / **Démobiliser** | `Core/Keyed/GameplayCommands.xml` (`CommandDraftLabel`, `CommandUndraftLabel`) |
| bill (a work order) | facture, note | **tâche** | `Core/Keyed/ITabs.xml` (`TabBills`), `Core/Keyed/ITabs.xml` (`AddBill`) |
| warden | gardien, directeur | **geôlier** | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.pawnLabel`) |
| hauling | remorquage, halage | **transport** / **Transporter** | `Core/Keyed/Designators.xml` (`DesignatorHaulThings`), `WorkTypes.xml` (`Hauling.*`) |
| raid | rafle, descente, attaque | **raid** (keep the loanword) | `Core/Keyed/Misc_Gameplay.xml` (`Raid`) |
| mood | ambiance, moral | **Humeur** | `Core/Keyed/Misc_Gameplay.xml` (`Mood`) |
| thought | idée, réflexion | **pensée** (distinct from «humeur») | `Core/Keyed/Dialogs_Various.xml` |
| memory (social) | mémoire | **souvenir** | `Anomaly/Keyed/Letters.xml` |
| quest | recherche, mission | **quête** | `Core/Keyed/Misc_Gameplay.xml` (`Quest`), `Core/DefInjected/MainButtonDef/*` (`Quests.label`) |
| settlement | règlement, accord, peuplement | **colonie** (player's) / **base de faction** (other's) | `Core/Keyed/GameplayCommands.xml` (`CommandAttackSettlementDesc`), `Core/Keyed/Letters.xml` (`LetterRelatedPawnsTradingWithSettlement`) |
| map | plan | **carte** | `Core/Keyed/Misc_Gameplay.xml` (`Map`) |
| map cell / square | cellule | **case** (reserve «cellule» for a *table* cell) | `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip`) |
| save (a game) | épargner, économiser | **Sauver** (button) / **sauvegarde** (the file) | `Core/Keyed/Menus_Main.xml` (`Save`) |
| load | téléverser, télécharger | **Charger** | `Core/Keyed/Menus_Main.xml` (`Load`) |
| deconstruct | déconstruire | **Démolir** | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| smooth (a surface) | lisser, adoucir | **Aplanir** | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface`) |
| claim | réclamer, prétendre | **Revendiquer** | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| strip (a pawn) | dépouiller, bande | **Déshabiller** | `Core/Keyed/Designators.xml` (`DesignatorStrip`) |
| forbid / allow | défendre / permettre | **Interdire** / **Autoriser** | `Core/Keyed/Designators.xml` (`DesignatorForbid`, `DesignatorUnforbid`) |
| tend (a wound) | tendre, s'occuper | **Soigner** | `Core/Keyed/FloatMenu.xml` (`Tend`) |
| tame | domestiquer | **Apprivoiser** | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | massacrer | **Abattre** | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| man (a turret) | homme | **Manipuler** | `Core/Keyed/FloatMenu.xml` (`OrderManThing`) |
| trade | échange (as verb) | **Commercer** (verb) / **Échanger avec {0}** (with a settlement) | `Core/Keyed/FloatMenu.xml` (`TradeWith`), `Core/Keyed/FloatMenu.xml` (`TradeWithSettlement`) |
| silver | argenté | **argent** (the resource) | `Core/Keyed/Dialogs_Various.xml` (`YourTradeableSilver`) |
| policy (drug/apparel) | politique, police | **règles** | `Core/Keyed/MainTabs.xml` (`ManageDrugPolicies`) |
| stockpile | pile, tas | **zone de stockage** | `Core/Keyed/Misc_Gameplay.xml` (`Stockpile`) |
| dumping stockpile | zone de rejet | **décharge** | `Core/Keyed/Designators.xml` (`DesignatorZoneCreateStorageDumpingDesc`) |
| growing zone | zone de croissance | **Zone agricole** | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone`) |
| recreation (need) | récréation | **plaisir** (the need) / **loisirs** (the build category) | `Core/DefInjected/NeedDef/Needs.xml` (`Joy.label`), `Core/DefInjected/DesignationCategoryDef/*` (`Joy.label`) |
| room size (need) | taille de la salle | **espace** | `Core/DefInjected/NeedDef/Needs.xml` (`RoomSize.label`) |
| masterwork (quality) | chef-d'œuvre | **merveille** | `Core/Keyed/Enums.xml` (`QualityCategory_Masterwork`) |
| techprint | impression technique | **schéma technique** | `Core/Keyed/Misc.xml` (`TechprintLabel`) |
| mech | mécanique | **mécanoïde** (also the short **mech**) | `Biotech/Keyed/Alerts.xml` (`AlertMechLacksOverseerDesc`) |
| psyfocus | psychofocus | **Concentration psychique** | `Royalty/Keyed/Misc_Gameplay.xml` (`Psyfocus`) |
| ideoligion | idéologie | **idéoligion** (a coined word — keep it) | `Ideology/Keyed/Misc_Gameplay.xml` (`Ideo`) |
| entity (Anomaly) | entreprise, organisme | **entité** | `Anomaly/Keyed/Misc_Gameplay.xml` (`EntitiesSection`) |
| record(s) tab | dossier, disque | **Enregistrement** | `Core/Keyed/ITabs.xml` (`TabRecords`) |
| storyteller | conteur | **Narrateur** | `Core/Keyed/MainTabs.xml` (`Storyteller`) |
| west / northwest (short) | W / NW | **O** / **NO** (and **SO** for southwest) | `Core/Keyed/Enums.xml` (`Direction8Way_*_Short`) |
| toggle | permuter, basculer entre | **Activer/Désactiver** | `Core/Keyed/GameplayCommands.xml` (`CommandToggleStudy`) |
| screen reader | liseuse d'écran | **lecteur d'écran** | standard French term (§2) |

---

## Section 3.8 — Pilot addenda (locked during the Common+Map pilot; inherit, don't re-derive)

**Articles before a direction/label placeholder — elision-safe composition.** Write the UNCONTRACTED
article and let the worker fix it: «vers le {0}», «depuis le {0}», «à le {0}» all resolve correctly
at runtime («le est» → «l'est» via ElisionE, which runs BEFORE the à-le rewrite; «à le nord» → «au
nord»). Never hand-write the contracted «au {0}» — «au est» is ungrammatical and NO rewrite fixes it.

**Compass compounds are hyphenated.** Diagonal compositions must join with a hyphen («{0}-{1}» →
«nord-est»), never bare concatenation («nordest»). Bare direction words: nord, sud, est, ouest,
nord-est, nord-ouest, sud-est, sud-ouest (lowercase).

**Additional locked terms** (corpus-anchored during the pilot):

| English | Français | Anchor |
|---|---|---|
| bookmark | signet | mod-coined; standard French computing term |
| chunks (rock) | gravats | `Core/DefInjected/TerrainDef` (`ChunkGranite.label` gravats en granite) |
| blight | mildiou | `Core/Keyed/Letters.xml` (`LetterLabelCropBlight`) |
| hopper | réservoir | `Core/DefInjected/ThingDef` (`Hopper.label`) |
| info card | onglet d'information | `Core/DefInjected/ConceptDef` (`InfoCard.label`) |
| time speeds | normale / rapide / très rapide / ultra rapide | `Core/DefInjected/KeyBindingDef` (`TimeSpeed_*.label`) |
| idle | inactif | `Core/Keyed/Alerts.xml` (`ColonistIdle`) |
| deep ore deposit | gisement de {0} | Core quest text (un gisement de […]) |
| interaction spot | point d'interaction | `Core/Keyed` (`InteractionSpotWillOverlap`) |
| work left | travail restant | `Core/Keyed` (`WorkLeft`) |
| smooth / rough (terrain) | poli / brut | `Core/DefInjected/TerrainDef` (`Granite_Smooth.label`, `Granite_Rough.label`) |
| chemfuel | chemfuel (loanword, untranslated) | corpus «réserve de chemfuel» |
| Shift key | Maj | `Core/Keyed` (`WorkPriorityShiftClickTip`); Home stays «Home» |
| world tile vs map cell | tuile (world) / case (map) | `CannotLandOnSameTile` vs `SelectNextInSquareTip` — keep vanilla's split |
| overhead mountain / thin rock | Montagne / Roche fine | `Core/DefInjected/RoofDef` (`RoofRockThick.label`, `RoofRockThin.label`) |

---

## Section 4 — How to extend this glossary

To find how RimWorld translates a term you don't see above (paths relative to the extracted corpus
root):

```bash
# UI strings (button labels, menu text, gameplay commands):
grep -rh 'EnglishKeyName' <fr_ref>/*/Keyed/*.xml

# Or search by a French word you suspect:
grep -rh 'mot_français' <fr_ref>/*/Keyed/*.xml

# Concept labels (things, designators, factions, skills, needs, biomes…):
grep -rh 'EnglishKeyName' <fr_ref>/*/DefInjected/**/*.xml

# How the official translation composed an article across a placeholder:
grep -rnE "\b(de|du|des|à|le|la|les) \{[A-Za-z0-9_]+\}" <fr_ref>/

# How it avoided gender agreement on a placeholder (the colon shape):
grep -rnE "<[A-Za-z0-9_]+>[A-ZÉÀ][a-zéèêàûôç ]{3,25} : \{" <fr_ref>/*/Keyed/*.xml
```

Useful sub-paths: `Keyed/Designators.xml` (order verbs), `Keyed/GameplayCommands.xml` (gizmos and
commands), `Keyed/FloatMenu.xml` (right-click actions — the richest source of the infinitive+object
shape), `Keyed/Misc.xml` + `Keyed/Misc_Gameplay.xml` (general UI), `Keyed/Enums.xml` (every enum
label: quality, price, storage priority, tech level, medical care, hunger, compass directions),
`Keyed/Dialogs_Various.xml` (dialog buttons), `Keyed/Menus_Main.xml` (main menu, enable/disable),
`Keyed/Menu_Options.xml` (settings vocabulary), `Keyed/Menu_KeyBindings.xml` (keys and bindings),
`Keyed/Time.xml` + `Keyed/Dates.xml` (calendar), `Keyed/Skills.xml`, `Keyed/ITabs.xml` (inspect
tabs), `Keyed/MainTabs.xml` (work/assign tabs), `Keyed/Messages.xml` (the `Noun : {0}` shape),
`DefInjected/MainButtonDef` (main tab names), `DefInjected/WorkTypeDef`, `DefInjected/SkillDef`,
`DefInjected/NeedDef`, `DefInjected/DesignationCategoryDef`, `DefInjected/WeatherDef`.

Many corpus files carry `<!-- EN: … -->` comments giving the English source above each value (see
`Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml`). Use them to confirm a key's
meaning — and, for anything involving a placeholder, to see exactly how the official translators
restructured the English sentence. Always cite the file you took a term from when you add a row.

## Mod-coined terms — watermill placement + read-only browsing (2026-08-13)

Thirteen new keys (`RimWorldAccess.Building.Place.Spot*`, `.Watermill*`, `RimWorldAccess.TextInput.BrowsingField/ReadOnlyField`)
had no reference translation to check: this local install only carries the English Core language pack,
so RimWorld's own French wording for the `WatermillGenerator` ThingDef could not be verified against the
game's corpus. Decided from context per house rule (no native-review parking); record here so later
waves stay consistent instead of re-deriving these.

| English | Français | Basis |
|---|---|---|
| watermill (whole building) | moulin à eau | Standard French term for a water mill |
| waterwheel (the wheel part) | roue à eau | Standard French term for a water wheel |
| moving/running water | eau courante | Standard idiom for flowing water |
| water flow area | zone de flux d'eau | Coined; parallels `Zone requise` (`OutlineOkAt`) |
| unshared / shared with another watermill | non partagée / partagée avec un autre moulin à eau | Coined |
| placement spot (scanner category/item) | Sites de placement : {0} / Emplacement | Mirrors `Abilities.Plant.ScannerCategory`/`ScannerItemLabel` ("Sites de plantation : {0}" / "Emplacement plantable") |
| facing {0} (trailing fragment) | orienté vers {0} | Mirrors `Building.ArchitectPlace.GravshipFacing` ("orienté vers {0}") |
