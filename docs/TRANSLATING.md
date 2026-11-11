# Translating FrameShift — start here

You do not need C#, Git, .NET, or the build tools. A maintainer gives you a folder
for one existing language. Work on a copy and return that folder to the maintainer.
The maintainer checks and applies your contribution; opening the CSV does not
change FrameShift on your computer.

1. Check `READY.txt` for the language. If it is missing, ask for a complete folder.
2. Import `translation.csv` into your spreadsheet editor as **UTF-8**, separator
   **semicolon**, quote character `"`, with **all columns set to Text**. Use the
   text/CSV import dialog rather than double-clicking. Keep the original copy.
3. Read `Source` (English) and `Context`, then edit **Translation only**. Leave
   Language, Scope, Key, Source, Tokens and SourceHash unchanged. Sort whole rows
   if useful, never a single column. Keep every row, including unfinished rows.
4. Preserve variables such as `{0}`, `{1:0.0}`, `%1`, `%n`, `{app}`. You can move
   them within a sentence, but retain each occurrence and its format. Do not
   change CLI values such as `light`, model IDs, product names or file extensions.
5. Consult `GLOSSARY.md` to distinguish ambiguous media terms. Do not guess if a
   term is unclear: list the Scope/Key reference in `FEEDBACK.txt`.
6. If `review.json` is supplied, prioritize `new`, `source-changed`, `context-changed`
   and `blank` entries. Old translations remain in the CSV as suggestions; a
   changed English sentence **requires a fresh reading**, even if the translation
   looks complete. Removed entries are historical and need no translation.
7. Leave unfinished translations blank. The importer preserves the existing text
   for those cells. Explorer captions appear in both app and installer rows:
   changing one is enough; avoid giving the two rows different translations.
8. Save as a **new UTF-8 semicolon CSV**, keeping line breaks and quotes. Do not
   rename an XLSX file to CSV. Reopen it and check accented/non-Latin characters,
   multiline cells, variables and the row count. Note the editor/version and
   results in `FEEDBACK.txt`.
9. Return the folder with your edited CSV and feedback through the contribution
   channel agreed with the maintainer. No account on a translation service is
   required. Leave `snapshot.json` untouched; it identifies the supplied texts.

The maintainer will report technical errors using Scope/Key and CSV record number.
Record numbers include the header and can differ from physical text line numbers
when cells span lines. A successful automated check does not approve the wording.
Native-language proofreading and a visual check in FrameShift are still needed.

Choose concise, natural wording. Distinguish destructive actions from creating a
new file. Preserve meaningful whitespace and deliberate line breaks. Ask for a
screenshot or explanation when Context only names an application area; those
automatic labels are not a substitute for a real description of the window.
