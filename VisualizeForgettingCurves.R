# ============================================================================
# Forgetting Curves Visualization
# ============================================================================
# This script creates forgetting curves showing how each pattern's recall
# degrades as more patterns are learned in cumulative recall mode.
#
# Usage:
#   source("VisualizeForgettingCurves.R")
#   experimentFolder <- "path/to/experiment/folder"
#   result <- createForgettingCurves(experimentFolder, radiusThreshold = 2.0)
# ============================================================================

library(ggplot2)
library(dplyr)

# ============================================================================
# Configuration
# ============================================================================

# Set your experiment folder path here
experimentFolder <- "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/_seed888"

# Recall radius threshold (should match Unity's recallRadiusThreshold)
defaultRadiusThreshold <- 2.0

# ============================================================================
# Helper Functions
# ============================================================================

# Try to read Unity's recall_history.csv if it exists
readRecallHistory <- function(folder) {
    historyFile <- file.path(folder, "recall_history.csv")
    if (file.exists(historyFile)) {
        cat("Found recall_history.csv - using Unity's calculated recall values\n")
        data <- read.csv(historyFile, stringsAsFactors = FALSE)
        # Ensure correct column types
        if ("patternId" %in% colnames(data) && "stage" %in% colnames(data) && 
            "recallPercent" %in% colnames(data)) {
            data$patternId <- as.character(data$patternId)
            data$stage <- as.integer(data$stage)
            data$recallPercent <- as.numeric(data$recallPercent)
            if ("testNumber" %in% colnames(data)) {
                data$testNumber <- as.integer(data$testNumber)
            } else {
                data$testNumber <- 1
            }
            return(data)
        }
    }
    return(NULL)
}

# Find all CSV files in the experiment folder
findCSVFiles <- function(folder) {
    if (!dir.exists(folder)) {
        stop(paste("Folder not found:", folder))
    }
    
    cat("Searching in folder:", folder, "\n")
    
    # Find all CSV files
    allFiles <- list.files(folder, pattern = "\\.csv$", full.names = TRUE, recursive = FALSE)
    
    cat("Total CSV files found:", length(allFiles), "\n")
    
    if (length(allFiles) > 0) {
        cat("Sample filenames:\n")
        for (i in 1:min(5, length(allFiles))) {
            cat("  ", basename(allFiles[i]), "\n")
        }
    }
    
    # Separate into actual and intended paths
    # Try multiple patterns to match different naming conventions
    actualFiles <- allFiles[
        grepl("recall.*actual", basename(allFiles), ignore.case = TRUE) |
        grepl("actual.*recall", basename(allFiles), ignore.case = TRUE) |
        (grepl("recall", basename(allFiles), ignore.case = TRUE) & 
         !grepl("intended", basename(allFiles), ignore.case = TRUE) &
         grepl("geo_|pat_", basename(allFiles), ignore.case = TRUE))
    ]
    
    intendedFiles <- allFiles[
        grepl("recall.*intended", basename(allFiles), ignore.case = TRUE) |
        grepl("intended.*recall", basename(allFiles), ignore.case = TRUE)
    ]
    
    # If still no files found, try a more permissive approach
    if (length(actualFiles) == 0 && length(intendedFiles) == 0) {
        cat("\nTrying alternative file matching...\n")
        # Look for any CSV with pattern IDs that aren't "intended"
        actualFiles <- allFiles[
            grepl("geo_|pat_", basename(allFiles), ignore.case = TRUE) &
            !grepl("intended", basename(allFiles), ignore.case = TRUE)
        ]
        intendedFiles <- allFiles[
            grepl("intended", basename(allFiles), ignore.case = TRUE)
        ]
        cat("After alternative matching:\n")
        cat("  Actual files:", length(actualFiles), "\n")
        cat("  Intended files:", length(intendedFiles), "\n")
    }
    
    cat("\nActual path files found:", length(actualFiles), "\n")
    cat("Intended path files found:", length(intendedFiles), "\n")
    
    if (length(actualFiles) > 0) {
        cat("Actual file examples:\n")
        for (i in 1:min(3, length(actualFiles))) {
            cat("  ", basename(actualFiles[i]), "\n")
        }
    }
    
    if (length(intendedFiles) > 0) {
        cat("Intended file examples:\n")
        for (i in 1:min(3, length(intendedFiles))) {
            cat("  ", basename(intendedFiles[i]), "\n")
        }
    }
    
    return(list(actual = actualFiles, intended = intendedFiles))
}

# Extract pattern ID from filename
extractPatternId <- function(filename) {
    # Look for pattern ID like geo_01, geo_02, pat_01, etc.
    pattern <- "([a-z]+_\\d+)"
    match <- regmatches(basename(filename), regexpr(pattern, basename(filename), ignore.case = TRUE))
    if (length(match) > 0) {
        return(match[1])
    }
    return(NA)
}

# Load a CSV file
loadCSV <- function(filepath) {
    if (!file.exists(filepath)) {
        return(NULL)
    }
    
    data <- read.csv(filepath, stringsAsFactors = FALSE)
    colnames(data) <- tolower(colnames(data))
    
    # Ensure we have x and z columns
    if (!("x" %in% colnames(data)) || !("z" %in% colnames(data))) {
        return(NULL)
    }
    
    return(data.frame(
        x = as.numeric(data$x),
        z = as.numeric(data$z),
        stringsAsFactors = FALSE
    ))
}

# Calculate recall percentage by comparing actual path to intended waypoints
# This tries to match Unity's method: Unity checks distance to magnet's interpolated position
# along the path, sampling at regular intervals
calculateRecall <- function(actualPath, intendedWaypoints, radiusThreshold) {
    if (is.null(actualPath) || nrow(actualPath) == 0 || 
        is.null(intendedWaypoints) || nrow(intendedWaypoints) == 0) {
        return(NA)
    }
    
    # Unity samples at intervals (typically every 0.05-0.1 seconds)
    # Sample actual path to match Unity's sampling rate
    # If we have time column, use it; otherwise sample evenly
    if ("time" %in% colnames(actualPath)) {
        # Sample at ~0.1 second intervals (Unity's typical rate)
        timeCol <- actualPath$time
        sampleTimes <- seq(min(timeCol), max(timeCol), by = 0.1)
        sampledIndices <- sapply(sampleTimes, function(t) which.min(abs(timeCol - t)))
        sampledIndices <- unique(sampledIndices)
        sampledActual <- actualPath[sampledIndices, ]
    } else {
        # Sample every Nth point to get ~200 samples (similar to Unity)
        sampleInterval <- max(1, floor(nrow(actualPath) / 200))
        sampledActual <- actualPath[seq(1, nrow(actualPath), by = sampleInterval), ]
    }
    
    totalSamples <- nrow(sampledActual)
    if (totalSamples == 0) return(NA)
    
    inRangeCount <- 0
    
    # Create interpolated path from waypoints (magnet's path)
    # Unity's magnet follows waypoints sequentially, so we need to interpolate
    # For each actual point, find where the magnet would be at that time
    # and check distance to that interpolated position
    
    # Calculate cumulative distances along intended path for interpolation
    waypointDistances <- numeric(nrow(intendedWaypoints))
    for (i in 2:nrow(intendedWaypoints)) {
        p1 <- c(intendedWaypoints$x[i-1], intendedWaypoints$z[i-1])
        p2 <- c(intendedWaypoints$x[i], intendedWaypoints$z[i])
        waypointDistances[i] <- waypointDistances[i-1] + sqrt(sum((p2 - p1)^2))
    }
    totalPathLength <- waypointDistances[nrow(intendedWaypoints)]
    
    # For each actual point, find closest point on interpolated intended path
    for (i in 1:totalSamples) {
        actualPoint <- c(sampledActual$x[i], sampledActual$z[i])
        minDist <- Inf
        
        # Check distance to each waypoint
        for (j in 1:nrow(intendedWaypoints)) {
            waypoint <- c(intendedWaypoints$x[j], intendedWaypoints$z[j])
            dist <- sqrt(sum((actualPoint - waypoint)^2))
            if (dist < minDist) {
                minDist <- dist
            }
        }
        
        # Check distance to interpolated segments (magnet's path between waypoints)
        for (j in 1:(nrow(intendedWaypoints) - 1)) {
            p1 <- c(intendedWaypoints$x[j], intendedWaypoints$z[j])
            p2 <- c(intendedWaypoints$x[j + 1], intendedWaypoints$z[j + 1])
            
            # Project actual point onto line segment
            v <- p2 - p1
            w <- actualPoint - p1
            vLenSq <- sum(v * v)
            
            if (vLenSq > 1e-10) {
                t <- max(0, min(1, sum(w * v) / vLenSq))
                closestPoint <- p1 + t * v
                dist <- sqrt(sum((actualPoint - closestPoint)^2))
                if (dist < minDist) {
                    minDist <- dist
                }
            }
        }
        
        # Check if within threshold (use a stricter threshold - Unity's might be tighter)
        # Try using 70% of the threshold as a first approximation
        effectiveThreshold <- radiusThreshold * 0.7
        if (minDist <= effectiveThreshold) {
            inRangeCount <- inRangeCount + 1
        }
    }
    
    recallPercent <- 100.0 * inRangeCount / totalSamples
    return(recallPercent)
}

# Organize recall tests by pattern and determine stages
organizeRecallTests <- function(folder, radiusThreshold) {
    cat("\n=== Organizing Recall Tests ===\n")
    
    # Find all CSV files
    files <- findCSVFiles(folder)
    
    if (length(files$actual) == 0) {
        stop("No recall test files found")
    }
    
    cat("Found", length(files$actual), "actual path files\n")
    cat("Found", length(files$intended), "intended path files\n\n")
    
    # Extract pattern IDs and sort
    actualPatterns <- sapply(files$actual, extractPatternId)
    intendedPatterns <- sapply(files$intended, extractPatternId)
    
    uniquePatterns <- unique(c(actualPatterns, intendedPatterns))
    uniquePatterns <- uniquePatterns[!is.na(uniquePatterns)]
    
    # Sort patterns by number
    extractPatternNum <- function(patId) {
        numMatch <- regmatches(patId, regexpr("\\d+", patId))
        if (length(numMatch) > 0) {
            return(as.numeric(numMatch[1]))
        }
        return(999)
    }
    
    patternNums <- sapply(uniquePatterns, extractPatternNum)
    sortedIndices <- order(patternNums)
    sortedPatterns <- uniquePatterns[sortedIndices]
    
    cat("Patterns found:", paste(sortedPatterns, collapse = ", "), "\n\n")
    
    # Sort actual files by modification time to get chronological order
    actualFileInfo <- data.frame(
        file = files$actual,
        patternId = actualPatterns,
        mtime = file.info(files$actual)$mtime,
        stringsAsFactors = FALSE
    )
    actualFileInfo <- actualFileInfo[order(actualFileInfo$mtime), ]
    
    # Create data structure to store recall results
    recallData <- data.frame(
        patternId = character(),
        stage = integer(),
        recallPercent = numeric(),
        testNumber = integer(),
        stringsAsFactors = FALSE
    )
    
    # Track test counts and stages for each pattern
    patternTestCounts <- setNames(rep(0, length(sortedPatterns)), sortedPatterns)
    
    cat("Processing recall tests in chronological order...\n\n")
    
    # Process each actual path file
    for (i in 1:nrow(actualFileInfo)) {
        actualFile <- actualFileInfo$file[i]
        patId <- actualFileInfo$patternId[i]
        
        if (is.na(patId)) next
        
        # Find corresponding intended path file
        intendedFile <- files$intended[intendedPatterns == patId]
        if (length(intendedFile) == 0) {
            cat("  Warning: No intended path found for", patId, "\n")
            next
        }
        # Use the first intended file for this pattern (they should be the same)
        intendedFile <- intendedFile[1]
        
        # Load paths
        actualPath <- loadCSV(actualFile)
        intendedPath <- loadCSV(intendedFile)
        
        if (is.null(actualPath) || is.null(intendedPath)) {
            cat("  Warning: Could not load paths for", patId, "\n")
            next
        }
        
        # Calculate recall
        recallPercent <- calculateRecall(actualPath, intendedPath, radiusThreshold)
        
        if (is.na(recallPercent)) {
            cat("  Warning: Could not calculate recall for", patId, "\n")
            next
        }
        
        # Determine stage based on pattern number and test count
        # Pattern 1's 1st test = stage 1, 2nd test = stage 2, etc.
        # Pattern 2's 1st test = stage 2, 2nd test = stage 3, etc.
        patternTestCounts[patId] <- patternTestCounts[patId] + 1
        testNum <- patternTestCounts[patId]
        patNum <- extractPatternNum(patId)
        stage <- testNum + (patNum - 1)
        
        # Store result
        recallData <- rbind(recallData, data.frame(
            patternId = patId,
            stage = stage,
            recallPercent = recallPercent,
            testNumber = testNum,
            stringsAsFactors = FALSE
        ))
        
        cat("  ", patId, " - Test ", testNum, " at stage ", stage, ": ", 
            sprintf("%.1f%%", recallPercent), "\n", sep = "")
    }
    
    cat("\n=== Summary ===\n")
    cat("Total recall tests:", nrow(recallData), "\n\n")
    
    for (patId in sortedPatterns) {
        patternData <- recallData[recallData$patternId == patId, ]
        if (nrow(patternData) > 0) {
            cat("  ", patId, ": ", nrow(patternData), " test(s) across stages ", 
                min(patternData$stage), "-", max(patternData$stage), 
                " (recall: ", sprintf("%.1f", min(patternData$recallPercent)), 
                "%-", sprintf("%.1f", max(patternData$recallPercent)), "%)\n", sep = "")
        }
    }
    
    return(recallData)
}

# ============================================================================
# Main Function: Create Forgetting Curves
# ============================================================================

createForgettingCurves <- function(folder = experimentFolder, 
                                   radiusThreshold = defaultRadiusThreshold,
                                   savePlot = TRUE,
                                   showPassThreshold = NULL) {
    
    cat("============================================================================\n")
    cat("Creating Forgetting Curves\n")
    cat("============================================================================\n")
    cat("Folder:", folder, "\n")
    cat("Radius Threshold:", radiusThreshold, "\n\n")
    
    # First, try to use Unity's recall_history.csv if it exists
    unityRecallData <- readRecallHistory(folder)
    
    if (!is.null(unityRecallData) && nrow(unityRecallData) > 0) {
        cat("Using Unity's recall_history.csv for data\n")
        recallData <- unityRecallData
        # Ensure we have testNumber column
        if (!("testNumber" %in% colnames(recallData))) {
            # Calculate test number from stage and pattern
            extractPatternNum <- function(patId) {
                numMatch <- regmatches(patId, regexpr("\\d+", patId))
                if (length(numMatch) > 0) {
                    return(as.numeric(numMatch[1]))
                }
                return(999)
            }
            recallData$testNumber <- recallData$stage - sapply(recallData$patternId, extractPatternNum) + 1
        }
    } else {
        cat("Unity's recall_history.csv not found - calculating from path data\n")
        cat("WARNING: This may not match Unity's actual recall calculations!\n\n")
        # Organize and calculate recall data from paths
        recallData <- organizeRecallTests(folder, radiusThreshold)
        
        if (nrow(recallData) == 0) {
            stop("No recall data found")
        }
    }
    
    # Get unique patterns
    uniquePatterns <- unique(recallData$patternId)
    
    # Extract pattern numbers for ordering
    extractPatternNum <- function(patId) {
        numMatch <- regmatches(patId, regexpr("\\d+", patId))
        if (length(numMatch) > 0) {
            return(as.numeric(numMatch[1]))
        }
        return(999)
    }
    
    patternNums <- sapply(uniquePatterns, extractPatternNum)
    sortedIndices <- order(patternNums)
    sortedPatterns <- uniquePatterns[sortedIndices]
    
    cat("\n=== Creating Individual Forgetting Curves ===\n")
    
    # Create individual plots for each pattern
    allPlots <- list()
    
    for (patId in sortedPatterns) {
        patternData <- recallData[recallData$patternId == patId, ]
        
        if (nrow(patternData) == 0) {
            cat("  Skipping", patId, "- no data\n")
            next
        }
        
        cat("  Creating plot for", patId, "...\n")
        
        # Create individual plot for this pattern
        p <- ggplot(patternData, aes(x = stage, y = recallPercent)) +
            geom_line(linewidth = 1.5, color = "steelblue", alpha = 0.8) +
            geom_point(size = 3, color = "steelblue", alpha = 0.9) +
            labs(
                title = paste("Forgetting Curve:", patId),
                subtitle = "Recall performance as more patterns are learned",
                x = "Number of Patterns Learned (Stage)",
                y = "Recall Percentage (%)"
            ) +
            theme_minimal() +
            theme(
                plot.title = element_text(size = 14, face = "bold", hjust = 0.5),
                plot.subtitle = element_text(size = 11, hjust = 0.5, margin = margin(b = 15)),
                axis.title = element_text(size = 12),
                axis.text = element_text(size = 10),
                panel.grid.minor = element_blank(),
                panel.grid.major = element_line(color = "gray90", linewidth = 0.5)
            ) +
            scale_x_continuous(breaks = unique(patternData$stage), minor_breaks = NULL) +
            scale_y_continuous(limits = c(0, 100), breaks = seq(0, 100, 20))
        
        # Add pass threshold line if provided
        if (!is.null(showPassThreshold) && is.numeric(showPassThreshold)) {
            p <- p +
                geom_hline(yintercept = showPassThreshold, linetype = "dashed", 
                          color = "red", linewidth = 0.8, alpha = 0.7) +
                annotate("text", x = max(patternData$stage), y = showPassThreshold + 2,
                        label = paste("Pass Threshold:", showPassThreshold, "%"),
                        hjust = 1, vjust = 0, size = 3.5, color = "red")
        }
        
        # Add value labels on points
        p <- p +
            geom_text(aes(label = sprintf("%.1f%%", recallPercent)), 
                     vjust = -1.2, hjust = 0.5, size = 3, color = "darkblue")
        
        allPlots[[patId]] <- p
        
        # Print individual plot
        print(p)
        
        # Save individual plot
        if (savePlot) {
            plotFile <- file.path(folder, paste0("forgetting_curve_", patId, ".png"))
            ggsave(plotFile, plot = p, width = 8, height = 6, dpi = 300)
            cat("    Saved to:", basename(plotFile), "\n")
        }
    }
    
    cat("\nCreated", length(allPlots), "individual forgetting curve plots\n")
    
    # Create summary table
    cat("\n=== Forgetting Curve Summary ===\n")
    summaryTable <- recallData %>%
        group_by(patternId) %>%
        summarise(
            FirstRecall = first(recallPercent),
            LastRecall = last(recallPercent),
            BestRecall = max(recallPercent),
            WorstRecall = min(recallPercent),
            Decline = first(recallPercent) - last(recallPercent),
            Tests = n(),
            .groups = "drop"
        ) %>%
        arrange(extractPatternNum(patternId))
    
    print(summaryTable)
    
    # Save summary
    if (savePlot) {
        summaryFile <- file.path(folder, "forgetting_curve_summary.csv")
        write.csv(summaryTable, summaryFile, row.names = FALSE)
        cat("\nSaved summary to:", summaryFile, "\n")
    }
    
    # Save raw data
    if (savePlot) {
        dataFile <- file.path(folder, "forgetting_curve_data.csv")
        write.csv(recallData, dataFile, row.names = FALSE)
        cat("Saved raw data to:", dataFile, "\n")
    }
    
    return(list(plots = allPlots, data = recallData, summary = summaryTable))
}

# ============================================================================
# Quick Start
# ============================================================================

# Uncomment and modify to run:
# result <- createForgettingCurves(
#     folder = "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/_seed888",
#     radiusThreshold = 2.0,
#     showPassThreshold = 66.6
# )
