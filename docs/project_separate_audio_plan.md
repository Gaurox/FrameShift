---
name: separate-audio-plan
description: "Plan d'intégration Audio Separation (HTDemucs ONNX) dans FrameShift — 11 lots, décisions validées (mai 2026)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 174b92cf-56c6-45b8-a75f-88344e71ae3f
---

# FrameShift AI — Audio Separation (HTDemucs)

## Décisions validées
- 2 modèles : V1 CPU (`htdemucs.onnx`) + V2 GPU split (`htdemucs_split.onnx`), sélection auto DML dispo
- Download du seul modèle nécessaire à la machine
- SHA256 V1 CPU : `A56BEF35B4C1B776502A53A36D4E1CAC1CB903BD9AE225939668E310B6DB1D44`
- SHA256 V2 GPU : `7776FA928E69720966694CDF622A7F887AF543EF67E23A31979AAB81F0C97206`
- URL V1 CPU : https://huggingface.co/Gaurox/frameshift-models/resolve/main/htdemucs/htdemucs.onnx
- URL V2 GPU : https://huggingface.co/Gaurox/frameshift-models/resolve/main/htdemucs-split/htdemucs_split.onnx
- Taille V1 CPU : 289 MB ; V2 GPU : 161 MB
- Session ONNX partagée entre fichiers d'un batch (comme Remove Background)
- Resample silencieux NAudio → toujours 44.1 kHz stéréo PCM_16 en sortie
- Outputs : `<base>_vocals.wav`, `<base>_drums.wav`, etc. via OutputPathHelper
- NuGet ajouté : NAudio

## Constantes OLA (du guide Demucs)
- LEN = 343_980 samples (~7.8 s à 44.1 kHz)
- hop = LEN × 0.75 = 257_985 samples (overlap 25%)
- Fenêtre triangulaire (pas Hann)
- K = ceil((N - LEN) / hop) + 1 chunks, ou 1 si N ≤ LEN

## Pipeline V1 CPU par chunk
ReadRange → session.Run({mix}) → 4 stems → OLA ring → flush → AudioStemWriter

## Pipeline V2 GPU par chunk
ReadRange → HostSpectro.Stft → session.Run({mix, spec}) → HostSpectro.Istft → stems_time+x_freq → OLA ring → flush

## HostSpectro — invariants critiques
- le = ceil(L / hop_length) (jamais L // hop_length)
- y = y_real + y_imag (le kernel_imag porte déjà -sin)
- reflect-pad 1536 gauche / (1536 + le*1024 - L) droite
- scale_k[0]=scale_k[-1]=1, scale_k[1:-1]=2

## Lots
- **Lot 1** : Infrastructure Core/AI/SeparateAudio (squelette), ActionRegistry, Program.cs minimal, NAudio NuGet
- **Lot 2** : DownloadModelForm généralisé + EnsureSeparateAudioModelReady
- **Lot 3** : AudioChunkReader + AudioStemWriter (NAudio I/O)
- **Lot 4** : OverlapAddRing (OLA ring buffer streaming)
- **Lot 5** : HostSpectro (STFT/iSTFT C# — port de stft_onnx.py) — point de risque max
- **Lot 6** : AudioSeparationEngine V1 CPU bout-en-bout → MVP
- **Lot 7** : AudioSeparationEngine V2 GPU + sélection auto DML/CPU
- **Lot 8** : SeparateAudioPickerForm (UI 560×440, 5 checkboxes, radio Engine)
- **Lot 9** : Câblage Program.cs complet + ConversionBatchSession définition
- **Lot 10** : Installer (composant ai\separate_audio, menu Explorer AudioExtensions)
- **Lot 11** : Tests d'intégration manuels + polish

## Statut des lots
- Lot 1 : en cours
