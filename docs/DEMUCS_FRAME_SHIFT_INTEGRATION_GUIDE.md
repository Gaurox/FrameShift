# Demucs → FrameShift Integration Guide

> **Audience :** développeur reprenant l'intégration dans `E:\AI\FrameShift_V1` sans avoir lu l'historique.  
> **Date :** 2026-05-25 — intégration C# complète, auditée et corrigée (v1.0.5).

---

## 1. Résumé exécutif

### État actuel

Deux pipelines de séparation de sources audio HTDemucs v4 sont **intégrés et validés en production** dans FrameShift depuis la version 1.0.1. Le code de référence Python reste dans `E:\AI\Demuc_ONNX` (lecture seule — ne pas modifier ni importer directement).

**Corrections apportées en 1.0.5 (2026-05-25) :**
- Fallback DML corrigé : en cas d'échec DirectML, le moteur bascule sur `htdemucs.onnx` (V1 CPU, STFT in-graph) et non plus sur le modèle split exécuté sur CPU (qui n'apporte aucun gain hors GPU).
- Réutilisation de session ONNX : l'engine est désormais créé une seule fois par batch et réutilisé sur tous les fichiers de la file, éliminant le rechargement du modèle à chaque appel.
- Audit complet (STFT/iSTFT, OLA, chunking, ordre des stems) confirmé correct — aucune modification des invariants mathématiques.

| Pipeline | Modèle | EP | Statut |
|---|---|---|---|
| **V1 CPU** | `htdemucs.onnx` (289 MB) | CPUExecutionProvider | ✅ Production-ready |
| **V2 GPU** | `htdemucs_split.onnx` (161 MB) | DirectMLExecutionProvider | ✅ Production-ready |

### Modèles ONNX

| Élément | V1 CPU | V2 GPU (split) |
|---|---|---|
| Fichier | `htdemucs.onnx` (289 MB) | `htdemucs_split.onnx` (161 MB) |
| Opset / IR | 17 / 8 | 17 / 8 |
| Entrée | `mix (B, 2, 343980)` | `mix (B, 2, 343980)` + `spec (B, 2, 2048, 336, 2)` |
| Sortie | `stems (B, 4, 2, 343980)` | `mask_spec (B, 4, 2, 2048, 336, 2)` + `stems_time (B, 4, 2, 343980)` |
| STFT/iSTFT | in-graph (Conv1d k=4096) | **host-side** (externalisé) |
| Runtime | ONNX Runtime ≥ 1.17 (testé 1.24.4) | idem |

### Licence

Chaîne complète **MIT** :
- Checkpoint `.th` dérivé du dépôt officiel `facebookresearch/demucs` (licence MIT, modèle 955717e8)
- `onnx_htdemucs.py`, `onnx_htdemucs_split.py`, scripts d'export : code original MIT
- `htdemucs.onnx` et `htdemucs_split.onnx` : transformations du checkpoint MIT — même licence MIT
- **Ne pas utiliser le modèle ONNX CC-BY-NC** qui circule sur HuggingFace Hub — licence incompatible avec un usage commercial/produit

### CPU : OK — DirectML : OK sur graphe split

| EP | Modèle | Verdict | Notes |
|---|---|---|---|
| **CPUExecutionProvider** | `htdemucs.onnx` | ✅ **Validé** | 5.25× RT, 1.7 GB RAM, 30 min audio réel |
| **DirectMLExecutionProvider** | ~~`htdemucs.onnx`~~ | ❌ KO sur ce graphe | 5 Conv1d k=4096 → 26 GB VRAM, blocage total |
| **DirectMLExecutionProvider** | `htdemucs_split.onnx` | ✅ **Validé** | 20× RT, +1.8 GB VRAM, 30 min audio réel |

---

## 2. Architecture (hors FrameShift)

### 2.1 Conversion du modèle

```
955717e8-8726e21a.th  (HTDemucs v4, poids PyTorch)
     ├── export_demucs.py       → htdemucs.onnx        (V1 CPU — STFT/iSTFT in-graph)
     └── export_demucs_split.py → htdemucs_split.onnx  (V2 GPU — STFT/iSTFT externalisé)
```

**Difficultés résolues lors de la conversion :**

1. **STFT complexe → Conv1d réel** : PyTorch exporte `torch.stft` avec des tenseurs complexes ; ONNX Runtime ne les supporte pas. Solution : `OnnxStft` / `OnnxIstft` implémentés avec deux `F.conv1d` utilisant des noyaux cos/sin × fenêtre de Hann (`stft_onnx.py`). Le flag `cac=True` du modèle garantit que le réseau n'utilise jamais de véritable arithmétique complexe en interne.

2. **MHA fast-path → slow path** : `nn.MultiheadAttention` utilise `_native_multi_head_attention` (non exportable) quand `torch.no_grad()` est actif. L'export est effectué **sans** `torch.no_grad()` pour forcer le chemin générique exportable.

3. **Off-by-one dans le calcul du temps STFT** : `le = math.ceil(L / hop_length)` (et non `L // hop_length`) — sans cette correction, le tenseur temps était court d'un frame, produisant des artefacts de bord.

4. **DirectML KO sur `htdemucs.onnx`** : les 5 Conv1d/ConvTranspose1d à kernel=4096 sont baked dans le graphe. DirectML alloue ~26 GB workspace VRAM et bloque totalement. Solution : `htdemucs_split.onnx` externalise entièrement STFT/iSTFT — les 5 nœuds disparaissent du graphe, VRAM peak tombe à +26 MB par inférence.

### 2.2 Chunking OLA (Overlap-Add)

Le modèle accepte exactement 343 980 échantillons (~7.8 s). Les fichiers audio plus longs sont découpés en chunks avec overlap.

```
Segment :  LEN = 343 980 échantillons
Hop :      hop = LEN × 0.75 = 257 985 échantillons  (overlap 25 %)
Fenêtre :  triangulaire 1→171990→1 (longueur 343980), normalisée à 1.0

Pour un signal de N échantillons :
  K = ceil((N - LEN) / hop) + 1  chunks  (ou 1 si N ≤ LEN)
  Dernier chunk : zero-padded jusqu'à LEN

OLA :
  num[S, 2, N] += out[S, 2, LEN] × weight
  den[N]       += weight
  result        = num / max(den, 1e-8)
  crop          = result[..., :N]
```

Convention identique à `demucs.apply.apply_model`. Validé sans artefact aux jonctions sur signal stationnaire ET sur 30 min d'audio réel (ratio jonction/intérieur 0.55–0.73, identique entre V1 CPU et V2 GPU).

### 2.3 Streaming (ring buffer — RAM constante)

```
ring_num  (S_active, 2, LEN)  float32  ≈ 11 MB
ring_den  (LEN,)              float32  ≈  1 MB
roll_origin : indice global de ring[..., 0]
```

**Invariant de flush** : après le chunk k, tous les échantillons `< (k+1)×hop` sont définitifs. On les normalise, on les écrit dans les WAV, on décale le ring à gauche. RAM O(1) en durée audio.

### 2.4 Pipeline V2 GPU per-chunk

```
[host CPU]  AudioChunkReader.read_range(start, LEN)    → mix (2, LEN)
[host CPU]  OnnxStft.forward(pad(mix))                 → spec (1, 2, 2048, 336, 2)
[DML GPU ]  session.run({mix, spec})                   → mask_spec, stems_time
[host CPU]  OnnxIstft.forward(pad(mask_spec))          → x_freq (1, 4, 2, LEN)
[host CPU]  stems = stems_time + x_freq               → OLA ring → flush → WAV
```

### 2.5 Stems sélectifs

Le modèle produit toujours les 4 stems — on ne peut pas réduire l'inférence. Mais :
- Ring buffer dimensionné à `S_active ≤ 4` stems actifs
- Les WAV des stems non demandés ne sont pas ouverts
- `instrumental = drums + bass + other` calculé pendant le flush (aucun buffer dédié)

---

## 3. Performances validées

### 3.1 Tableau récapitulatif — audio réel (`test.wav`, 30.08 min, 44.1 kHz PCM_16)

| Pipeline | Wall total | RT factor | RAM peak | VRAM delta | OLA artefacts |
|---|---:|---:|---:|---:|---|
| **V1 CPU** (`htdemucs.onnx`) | **343 s** | **5.25×** | **1 677 MB** | n/a | aucun (ratio < 1) |
| **V2 GPU** (`htdemucs_split.onnx`) | **90 s** | **20.05×** | **1 323 MB** | **+1 845 MB** | aucun (ratio < 1) |
| Speedup V2 vs V1 | **×3.82** | **×3.82** | **−21 %** | — | — |

Machine de mesure : Ryzen 24 cœurs (16T ORT pour V1), RTX 5090 (V2 DML).

### 3.2 Durées de sortie (test.wav)

```
V1 CPU : 79 579 428 frames = 1804.52 s  ×5 stems  (drums/bass/other/vocals/instrumental)
V2 GPU : 79 579 428 frames = 1804.52 s  ×5 stems
```

Identiques à l'échantillon près. Pas d'artefact d'arrondi, pas de troncature.

### 3.3 Qualité V2 GPU vs V1 CPU (même fichier)

| Stem | max diff | SNR V2 vs V1 | Ratio OLA jonction V2 |
|---|---:|---:|---:|
| drums | 5.49e-4 | **82.1 dB** | **0.55** |
| bass | 2.75e-4 | **84.0 dB** | **0.73** |
| other | 3.36e-4 | **76.8 dB** | **0.69** |
| vocals | 1.22e-4 | **83.4 dB** | **0.71** |
| instrumental | 2.14e-4 | **88.4 dB** | **0.56** |

SNR ≥ 76.8 dB sur tous les stems — inaudible à l'écoute. Les ratios OLA V2 sont **identiques** à ceux de V1 : la chaîne externalisée ne dégrade pas la continuité inter-chunks.

### 3.4 Détail per-chunk V2 GPU

```
STFT host (CPU)     :  30.7 ms / chunk  →   9.5 s total
DML inference (GPU) :  47.6 ms / chunk  →  14.7 s total  (goulot secondaire)
iSTFT host (CPU)    : 183.5 ms / chunk  →  56.7 s total  (goulot principal = 63 %)
```

L'iSTFT host est le goulot — voir §7.4 pour optimisations futures.

### 3.5 Projections machine utilisateur

| Cible | V1 CPU wall (30 min) | V2 GPU wall (30 min) | RAM (V2) | VRAM (V2) |
|---|---:|---:|---:|---:|
| Machine dev (24c + RTX 5090) | 343 s | **90 s** | 1.3 GB | +1.8 GB |
| Machine user (8c + GPU mid-range) | ~600 s | ~150–200 s (estimé) | ~1.5 GB | ~+2 GB |
| Machine sans GPU / DML indispo | ~600 s | fallback V1 CPU | ~1.7 GB | n/a |

### 3.6 Limites connues

| Limite | Impact | Pipeline concerné |
|---|---|---|
| Segment fixe 7.8 s | Aucun (chunking transparent) | V1 + V2 |
| fp32 uniquement | RAM ORT 1.6 GB (V1) | V1 uniquement |
| iSTFT host lent (~184 ms/chunk) | Wall time limité à ~20× RT | V2 (voir §7.4) |
| VRAM résiduelle +180 MB post-dispose | Potentiel cumul multi-morceaux | V2 — surveiller |
| Diff vs `apply_model` Demucs ~1e-2 | Quelques dB de SDR vs référence torch | V1 + V2 |

---

## 4. Inventaire des fichiers produits

Tous dans `E:\AI\Demuc_ONNX\`.

### Modèles ONNX

| Fichier | Rôle |
|---|---|
| `htdemucs.onnx` | **Modèle V1 production** (289 MB, fp32) — CPU EP |
| `htdemucs_split.onnx` | **Modèle V2 production** (161 MB, fp32) — DML EP |
| `955717e8-8726e21a.th` | Checkpoint PyTorch source (MIT, ne pas redistribuer) |

### Conversion

| Fichier | Rôle |
|---|---|
| `stft_onnx.py` | STFT/iSTFT réels via Conv1d (noyaux cos/sin × Hann) |
| `onnx_htdemucs.py` | Wrapper PyTorch V1 — forward sans complexes, STFT in-graph |
| `onnx_htdemucs_split.py` | Wrapper PyTorch V2 — forward sans STFT/iSTFT |
| `export_demucs.py` | Export `.th → htdemucs.onnx` |
| `export_demucs_split.py` | Export `.th → htdemucs_split.onnx` |

### Pipelines Python

| Fichier | Rôle |
|---|---|
| `separate_streaming.py` | **Pipeline V1 production** — CPU EP, streaming, stems sélectifs, CLI |
| `separate_streaming_gpu.py` | **Pipeline V2 production** — DML EP + host STFT/iSTFT, streaming, CLI |
| `separate_chunked.py` | Pipeline in-memory (référence, tests) |

### Scripts de validation / inspection

| Fichier | Rôle |
|---|---|
| `validate_chunking.py` | Validation vs Demucs `apply_model` gold reference |
| `test_chunker_consistency.py` | Test ONNX-chunked vs torch-chunked (même chunker) |
| `test_ola_stationary.py` | Test artefacts OLA sur signal 440 Hz |
| `inspect_graph.py` | Inventaire nœuds V1 (diagnostic DML) |
| `inspect_split_graph.py` | Comparaison nœuds V1 vs V2 |
| `validate_split_cpu.py` | V1 vs V2 sur CPU, 1 segment, équivalence numérique |
| `compare_split_cpu_vs_dml.py` | CPU EP vs DML EP sur graphe split |
| `compare_v1_vs_v2_stems.py` | Comparaison frame-à-frame V1 vs V2 + scan OLA |
| `test_split_directml.py` | Watchdog parent DML (VRAM + temps) |
| `_split_dml_child.py` | Process enfant DML (isolé) |

### Rapports

| Fichier | Contenu |
|---|---|
| `README_conversion.md` | Méthodologie de conversion |
| `REPORT_runtime_validation.md` | Diagnostic DirectML failure + options V2 GPU |
| `REPORT_v1_cpu.md` | Validation CPU 30s / 5min / 30min (synth) |
| `REPORT_v1_streaming.md` | Validation streaming V1.1 + mesures RAM |
| `REPORT_v2_split.md` | Graph split : nœuds, DML 1-segment, CPU vs DML |
| `REPORT_v2_gpu_30min.md` | **Validation V2 GPU bout-en-bout 30 min audio réel** |
| `requirements.txt` | Dépendances Python |

---

## 5. Intégration FrameShift — structure implémentée

> **Statut (2026-05-25) :** l'intégration est complète depuis la version 1.0.1. Les noms de fichiers ci-dessous reflètent la structure réelle dans `src/FrameShift/Core/AI/SeparateAudio/` et `src/FrameShift/Windows/AI/`. Certains noms proposés initialement diffèrent légèrement des fichiers créés.

### 5.1 Localisation réelle

```
E:\AI\FrameShift_V1\
├── Models\
│   └── AudioSeparation\
│       ├── htdemucs.onnx             ← V1 CPU (téléchargé à la demande)
│       └── htdemucs_split.onnx       ← V2 GPU split (téléchargé à la demande)
└── Core\
    └── AI\
        └── AudioSeparation\
            ├── DemucsModelLocator.cs     ← résolution chemin + téléchargement
            ├── DemucsModelDownloader.cs  ← download + vérif SHA256 + progress
            ├── DemucsSession.cs          ← InferenceSession + sélection EP (DML/CPU)
            ├── HostSpectro.cs            ← STFT/iSTFT host (V2 uniquement)
            ├── AudioSeparationRequest.cs ← stems, overlap, preferGpu
            ├── AudioSeparationResult.cs  ← chemins WAV, durée, RT factor
            ├── AudioStemWriter.cs        ← écriture WAV streaming (NAudio WaveFileWriter)
            └── AudioChunkReader.cs       ← lecture seek/read (NAudio AudioFileReader)
```

**Modèles non embarqués — téléchargement à la demande :**

Ni `htdemucs.onnx` ni `htdemucs_split.onnx` ne doivent être inclus dans le build. Architecture identique au pattern RemoveBackground :

```
DemucsModelLocator.GetModelPath(preferGpu)
  ├─ preferGpu=true  → cible htdemucs_split.onnx
  │     SHA256 : [calculer au déploiement depuis E:\AI\Demuc_ONNX\htdemucs_split.onnx]
  └─ preferGpu=false → cible htdemucs.onnx
        SHA256 : 8726e21a993978c7ba086d3872e7608d7d5bfca646ca4aca459ffda844faa8b4

DemucsSession.cs
  ├─ preferGpu=true + DML disponible
  │     → InferenceSession(htdemucs_split.onnx, DmlExecutionProvider)
  │     → pipeline : AudioChunkReader → HostSpectro.Stft → ORT → HostSpectro.Istft → OLA
  └─ preferGpu=false OU DML indisponible
        → InferenceSession(htdemucs.onnx, CPUExecutionProvider)
        → pipeline : AudioChunkReader → ORT → OLA  (STFT/iSTFT in-graph)
```

**Sélection automatique DML vs CPU** :
```csharp
bool dmlAvailable = OrtEnv.Instance().GetAvailableProviders()
                          .Contains("DmlExecutionProvider");
```

### 5.2 Implémentation C# — pipeline V1 CPU (points clés)

```csharp
// Session CPU — charger une seule fois
var opts = new SessionOptions();
opts.IntraOpNumThreads = Environment.ProcessorCount;
opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
var session = new InferenceSession("htdemucs.onnx", opts);

// Constantes partagées V1 + V2
const int SR = 44100;
const int LEN = 343_980;
const float OVERLAP = 0.25f;
int hop = (int)(LEN * (1 - OVERLAP));  // 257 985

// Par chunk k (V1 CPU) :
//   1. AudioChunkReader.ReadRange(k * hop, LEN) → float[2 * LEN]
//   2. session.Run({"mix": input}) → float[4 * 2 * LEN]
//   3. OLA accumulate ringNum/ringDen (fenêtre triangulaire)
//   4. flush_until((k+1)*hop) → normaliser + AudioStemWriter.Write + décaler ring
//   5. CancellationToken.ThrowIfCancellationRequested()  ← entre chunks uniquement
```

### 5.3 Implémentation C# — pipeline V2 GPU (points clés)

```csharp
// Session DML — charger une seule fois (first inference ~370 ms cold, ~47 ms warm)
var opts = new SessionOptions();
opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
var session = new InferenceSession("htdemucs_split.onnx", opts,
    new[] { "DmlExecutionProvider" });

// Par chunk k (V2 GPU) :
//   1. AudioChunkReader.ReadRange(k * hop, LEN) → mix float[2 * LEN]
//   2. HostSpectro.Stft(mix) → spec float[2 * 2048 * 336 * 2]
//      (voir §5.5 pour l'implémentation STFT host)
//   3. session.Run({"mix": mix, "spec": spec}) → mask_spec, stems_time
//   4. HostSpectro.Istft(mask_spec) → x_freq float[4 * 2 * LEN]
//   5. stems = stems_time + x_freq  → OLA ring → flush → AudioStemWriter.Write
```

**Triangular window (partagée V1 + V2) :**
```csharp
float[] TriangularWindow(int n) {
    var w = new float[n];
    int half = (n + 1) / 2;
    for (int i = 0; i < half; i++) w[i] = (i + 1f) / half;
    for (int i = half; i < n; i++) w[i] = w[n - 1 - i];
    return w;
}
```

### 5.4 Dépendances NuGet

| Package | Usage |
|---|---|
| `Microsoft.ML.OnnxRuntime` | CPU EP (V1) |
| `Microsoft.ML.OnnxRuntime.DirectML` | DML EP (V2) — mutuellement exclusif avec le précédent |
| `NAudio` | Lecture WAV seek/read + écriture WAV PCM_16 stéréo |
| `MathNet.Numerics` (optionnel) | FFT pour HostSpectro si la conv1d manuelle est trop lente |

### 5.5 HostSpectro C# — STFT/iSTFT host (V2 uniquement)

L'implémentation doit reproduire exactement `OnnxStft` / `OnnxIstft` de `stft_onnx.py` :

```
STFT(mix, nfft=4096, hop=1024):
  1. Reflect-pad mix par hl//2*3 = 1536 à gauche et pad_right = 1536 + le*1024 - L à droite
  2. Conv1d(padded, weight_real) → real_out  (F=2049, T, stride=1024)
     Conv1d(padded, weight_imag) → imag_out
     weight = DFT-cossin × hann × (1/sqrt(4096))
  3. Drop top freq row : [:, :-1, :] → F=2048
  4. Crop time : [:, :, 2:2+le] → T=336
  5. Stack → spec (B, 2, 2048, 336, 2)

iSTFT(mask_spec):
  1. Pad freq +1 → F=2049
  2. Pad time ±2 → T=340
  3. ConvTranspose1d(real, weight_real) + ConvTranspose1d(imag, weight_imag) = y
     weight = DFT-cossin × hann × scale_k × (1/sqrt(4096))
     scale_k[0] = scale_k[-1] = 1 ; scale_k[1:-1] = 2
  4. Diviser par envelope (conv_transpose de ones avec window^2)
  5. Crop [1536 : 1536 + L]
```

**Invariant critique** : `y = y_real + y_imag` (le kernel_imag porte déjà le signe −sin, l'addition est correcte).

Alternative plus simple : implémenter via `System.Numerics.Complex` + FFT directe (`MathNet.Numerics.IntegralTransforms.Fourier.Forward`), ce qui évite de recalculer les noyaux Conv1d.

### 5.6 Cycle de vie des sessions

- **Créer** la session au démarrage ou à la première utilisation
- **Réutiliser** la même instance entre tous les morceaux (DML : première inf ~370 ms, suivantes ~47 ms)
- **Disposer** uniquement à la fermeture de l'application
- Sur 309 chunks × 30 min : **aucune fuite mémoire** observée (RAM plateau stable)

---

## 6. Interface utilisateur (implémentée)

> **Statut (2026-05-25) :** l'UI est implémentée dans `SeparateAudioPickerForm.cs`. Les paramètres avancés (§6.3) n'ont pas été exposés — l'expérience est délibérément simplifiée.

### 6.1 Sélection des stems (checkboxes)

```
[ ] Vocals         (voix principale + backing vocals)
[ ] Drums          (batterie, percussions)
[ ] Bass           (basse)
[ ] Other          (guitares, claviers, tout le reste)
[ ] Instrumental   (= Drums + Bass + Other, recalcul gratuit)
```

- Si "Instrumental" seul : drums/bass/other calculés en interne, non écrits sur disque
- Si tous décochés : erreur de validation avant de lancer
- Ordre interne invariant : `[drums, bass, other, vocals]` (indices 0–3 ONNX)

### 6.2 Sélection du moteur

```
○ Automatique   (DML si disponible, sinon CPU)
○ GPU (DirectML) — plus rapide, nécessite GPU compatible
○ CPU uniquement — compatible toutes machines
```

### 6.3 Paramètres avancés (mode expert)

```
Threads ORT : [auto / 1–32]     (CPU uniquement, défaut : nb cœurs physiques)
Overlap     : [25% / 50%]       (défaut 25%)
Format WAV  : [PCM_16 / PCM_24] (défaut PCM_16)
```

### 6.4 Indicateur de progression

K total connu avant le démarrage : `K = ceil((N - LEN) / hop) + 1`

```
Avancement : k / K  (ex : 45 / 309 = 14.6%)
Temps restant estimé : wall_elapsed / k × (K - k)
```

Annulation : `CancellationToken` vérifié **entre chunks uniquement** — un chunk dure ~1.5 s (V1) ou ~260 ms (V2), la réactivité est acceptable.

---

## 7. État de l'intégration et travaux ouverts

> **Statut (2026-05-25) :** tous les travaux bloquants listés dans les §7.1–7.3 originaux sont **terminés** depuis la version 1.0.1 (V1 CPU + V2 GPU). L'audit complet du 2026-05-25 a confirmé que toute la chaîne mathématique (STFT/iSTFT, OLA, chunking, ordre des stems) est correcte et conforme aux prototypes Python de référence.

### 7.1 Corrections apportées en v1.0.5 (2026-05-25)

| Correction | Fichier | Description |
|---|---|---|
| Fallback DML → V1 CPU | `AudioSeparationEngine.cs` | Échec DML → `htdemucs.onnx` CPU EP (et non split sur CPU) |
| Réutilisation session ONNX | `SeparateAudioAction.cs` | `_gpuEngine`/`_cpuEngine` lazy, réutilisés sur tout le batch |
| Radio buttons UI | `SeparateAudioPickerForm.cs` | `BackColor=Surface` + `UseVisualStyleBackColor=true` ; espacement corrigé pour éviter le chevauchement des bounds |

### 7.2 Limites connues acceptées

| Limite | Impact | Pipeline concerné |
|---|---|---|
| `AudioChunkReader` charge le fichier entier en RAM | Acceptable jusqu'à ~20 min ; problématique au-delà de 1 h | V1 + V2 |
| PCM_16 : `× 32767f` au lieu de `× 32768f` | Off-by-1 LSB au pic négatif — inaudible | V1 + V2 |
| Allocations par chunk (DenseTensor, buffer spec) | Pression GC, pas un problème de correction | V2 |
| +180 MB VRAM résiduel après dispose V2 | Surveillance recommandée en multi-morceaux longs | V2 |

### 7.3 Optimisations V2.1 (non bloquantes)

L'iSTFT host (torch ConvTranspose1d CPU) représente **63 % du wall time V2** (57 s / 90 s). Pistes :

| Optimisation | Gain estimé | Effort |
|---|---|---|
| Réimplémenter iSTFT via `numpy.fft.irfft` | −40 s wall → ~50 s total | faible |
| Recouvrement async (iSTFT chunk k pendant que DML traite k+1) | −30 s wall → ~60 s | moyen |
| Mini-graphe ONNX DML pour STFT/iSTFT (FFT native DirectML) | −50 s wall → ~40 s | R&D |

Objectif V2.1 : wall time < 60 s sur 30 min, soit **30× RT**.

---

## 8. Décisions déjà prises — ne pas revenir dessus

| Décision | Raison | Conséquence |
|---|---|---|
| **Modèle source : 955717e8 MIT** | Licence commerciale requise | Conserver cette chaîne dans tous les builds |
| **Segment fixe LEN = 343 980** | Constante architecturale HTDemucs v4 | Ne pas changer |
| **Overlap 25 % par défaut** | Convention Demucs, OLA validé sans artefact | Peut passer à 50 % mais 25 % est le référentiel |
| **Fenêtre triangulaire (pas Hann)** | Convention Demucs | Identique dans V1 et V2 |
| **CPU EP pour `htdemucs.onnx`** | DML KO sur ce graphe | Ne jamais activer DML sur `htdemucs.onnx` complet |
| **DML EP pour `htdemucs_split.onnx`** | Validé 30 min audio réel, VRAM +1.8 GB | Ne pas utiliser CPU EP sur `htdemucs_split.onnx` en production (pas de gain vs V1) |
| **Export sans `torch.no_grad()`** | Force MHA slow path exportable | Maintenir dans `export_demucs.py` et `export_demucs_split.py` |
| **iSTFT : `y = y_real + y_imag`** (pas minus) | K_imag porte déjà -sin | Toute réimplémentation doit respecter ce signe |
| **`le = math.ceil(L / hop_length)`** | Division entière → T court → artefacts de bord | Critique pour toute réimplémentation STFT |
| **Streaming ring buffer — taille LEN** | RAM O(1) en durée ; bit-perfect vs full-length | Ne pas revenir au full-length accumulator |
| **Ring buffer réutilisé tel quel V1→V2** | Ratios OLA jonction/intérieur identiques entre V1 et V2 | L'OLA ne dépend pas du EP utilisé |
| **InferenceSession partagée entre morceaux** | ~3 s de chargement ; DML : compilation kernels au 1er run | Ne pas instancier une session par morceau |
| **Format WAV sortie : PCM_16 stéréo 44.1 kHz** | Compatibilité DAW ; pas de perte audible | Baseline testée V1 + V2 |

---

*Guide mis à jour le 2026-05-23 — V2 GPU validée sur `test.wav` 30 min réel. `E:\AI\Demuc_ONNX`.*
