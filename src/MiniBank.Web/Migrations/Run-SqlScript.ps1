# PowerShell script to run SQL script with proper credentials
$server = "185.211.56.76,1463"
$database = "foodir_EmamAli_minibank"
$username = "foodir"
$password = "M2223125!"
$sqlScriptPath = "Migrations\RemoveUnusedBackupColumns.sql"

# Create connection string
$connectionString = "Server=$server;Database=$database;User Id=$username;Password=$password;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=true;"

# Read SQL script content and split by GO statements
$sqlScript = Get-Content -Path $sqlScriptPath -Raw
$sqlBatches = $sqlScript -split "\r\nGO\r\n" | Where-Object { $_.Trim().Length -gt 0 }

# Create SQL connection
$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)

try {
    $connection.Open()
    $command = $connection.CreateCommand()
    
    $totalRowsAffected = 0
    $batchNumber = 0
    
    foreach ($batch in $sqlBatches) {
        $batchNumber++
        $batch = $batch.Trim()
        if ($batch.Length -eq 0) { continue }
        
        try {
            $command.CommandText = $batch
            $rowsAffected = $command.ExecuteNonQuery()
            $totalRowsAffected += [Math]::Max(0, $rowsAffected)  # In case of -1 for DDL statements
            Write-Host "Batch #$batchNumber executed successfully. Rows affected: $rowsAffected"
            Write-Host "----------------------------------------"
        }
        catch {
            Write-Error "Error in batch #$batchNumber : $_"
            Write-Error "Batch content: $batch"
            throw  # Re-throw to stop execution on error
        }
    }
    
    Write-Host "SQL script executed successfully. Total rows affected: $totalRowsAffected"
}
catch {
    Write-Error "Error executing SQL script: $_"
}
finally {
    if ($connection.State -eq [System.Data.ConnectionState]::Open) {
        $connection.Close()
    }
}
