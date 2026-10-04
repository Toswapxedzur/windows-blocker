# The current native app requires the official-dictionary backend. A recent WPF
# build must not silently reuse a worker produced before this feature existed.
function Assert-VaultDictionaryWorker($Manifest) {
    $required=@(
        'Sources/VaultClassifierCore/OfficialDictionary.swift',
        'Sources/VaultClassifierCore/DictionaryDiskStore.swift',
        'Sources/VaultClassifierApp/OfficialDictionaryService.swift',
        'Sources/VaultClassifierApp/VaultClassifierViewModel+Dictionaries.swift'
    )
    foreach($path in $required) {
        $record=@($Manifest.sources | Where-Object { $_.path -eq $path })
        if($record.Count -ne 1 -or $record[0].sha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw "Classifier worker predates the dictionary feature; rebuild from the accepted shared source. Missing provenance: $path"
        }
    }
}
