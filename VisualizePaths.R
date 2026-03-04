# ============================================================================
# Path Visualization Script for Attractor Experiments
# ============================================================================
# This script visualizes intended vs actual paths from CSV experiment logs
# 
# Usage:
#   1. Set the experimentFolder path below to point to your experiment folder
#   2. Run: source("VisualizePaths.R")
#   3. Or run interactively in RStudio
# ============================================================================

# Load required libraries
if (!require("ggplot2")) {
    install.packages("ggplot2")
    library(ggplot2)
}
if (!require("dplyr")) {
    install.packages("dplyr")
    library(dplyr)
}
if (!require("gridExtra")) {
    install.packages("gridExtra")
    library(gridExtra)
}
if (!require("grid")) {
    install.packages("grid")
    library(grid)
}

# ============================================================================
# CONFIGURATION: Set your experiment folder path here
# ============================================================================
# Example: "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/CSV_Experiment1_seed42"
# Or use a relative path from your R working directory
experimentFolder <- "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/_seed727"

# ============================================================================
# Helper Functions
# ============================================================================

# Find all path files in the experiment folder
findPathFiles <- function(folder) {
    files <- list.files(folder, pattern = "\\.csv$", full.names = TRUE)
    
    # Separate intended and actual paths
    intendedFiles <- files[grepl("_intended_", files)]
    actualFiles <- files[!grepl("_intended_", files) & 
                         !grepl("experiment_summary", files) & 
                         !grepl("recall_summary", files)]
    
    # Extract pattern IDs and phases
    extractInfo <- function(filename) {
        basename <- basename(filename)
        parts <- strsplit(basename, "_")[[1]]
        
        phase <- ifelse(grepl("train", basename), "train", 
                       ifelse(grepl("recall", basename), "recall", "unknown"))
        
        # Extract pattern ID (everything between phase and timestamp/intended)
        # Handle formats like: "recall_geo_03_20260203_141200" or "recall_pat01_20260203_141200"
        patternId <- "unknown"  # Initialize with default value
        
        # Pattern: (train|recall)_(patternId)_(timestamp|intended)
        # Pattern ID can be "geo_03" or "pat01" - need to capture until timestamp/intended
        # Timestamp format: YYYYMMDD_HHMMSS (8 digits, underscore, 6 digits)
        # Or "intended" keyword
        
        # Try to match: phase_patternId_timestamp or phase_patternId_intended_timestamp
        # Extract everything between phase and timestamp/intended
        if (phase != "unknown") {
            # Find where phase ends
            phasePattern <- paste0("^", phase, "_")
            if (grepl(phasePattern, basename)) {
                # Remove phase prefix
                afterPhase <- sub(phasePattern, "", basename)
                
                # Now extract pattern ID (everything before timestamp or "intended")
                # Timestamp pattern: _YYYYMMDD_HHMMSS or _intended_
                # Pattern ID is everything before the first occurrence of _intended_ or _ followed by 8 digits
                if (grepl("_intended_", afterPhase)) {
                    # Pattern ID is before "_intended_"
                    patternId <- sub("_intended_.*$", "", afterPhase)
                } else {
                    # Pattern ID is before timestamp (8 digits, underscore, 6 digits)
                    patternId <- sub("_\\d{8}_\\d{6}.*$", "", afterPhase)
                }
                
                # Clean up: remove .csv if present
                patternId <- sub("\\.csv$", "", patternId)
            }
        }
        
        # Fallback: extract from filename structure
        if (patternId == "unknown" && phase != "unknown" && length(parts) >= 2) {
            # Find the phase index
            phaseIdx <- which(parts == phase)
            if (length(phaseIdx) > 0 && phaseIdx[1] < length(parts)) {
                # Pattern ID might be single part (pat01) or two parts (geo, 03)
                # Check if next part looks like a number (timestamp start)
                if (phaseIdx[1] + 1 < length(parts)) {
                    nextPart <- parts[phaseIdx[1] + 1]
                    # If it's "intended", skip it
                    if (nextPart == "intended" && phaseIdx[1] + 2 < length(parts)) {
                        patternId <- parts[phaseIdx[1] + 2]
                    } else if (!grepl("^\\d{8}$", nextPart)) {
                        # Not a timestamp, so it's part of pattern ID
                        # Check if pattern ID spans multiple parts (geo_03)
                        if (phaseIdx[1] + 2 < length(parts) && 
                            !grepl("^\\d{8}$", parts[phaseIdx[1] + 2])) {
                            patternId <- paste(parts[phaseIdx[1] + 1], 
                                             parts[phaseIdx[1] + 2], sep = "_")
                        } else {
                            patternId <- nextPart
                        }
                    }
                }
            }
        }
        
        return(list(phase = phase, patternId = patternId))
    }
    
    # Create data frame of files
    fileInfo <- data.frame(
        filename = c(intendedFiles, actualFiles),
        type = c(rep("intended", length(intendedFiles)), 
                 rep("actual", length(actualFiles))),
        stringsAsFactors = FALSE
    )
    
    # Extract info for each file
    infoList <- lapply(fileInfo$filename, extractInfo)
    fileInfo$phase <- sapply(infoList, function(x) x$phase)
    fileInfo$patternId <- sapply(infoList, function(x) x$patternId)
    
    return(fileInfo)
}

# Load a path CSV file
loadPathFile <- function(filepath, fileType) {
    if (!file.exists(filepath)) {
        warning(paste("File not found:", filepath))
        return(NULL)
    }
    
    data <- read.csv(filepath, stringsAsFactors = FALSE)
    
    # Normalize column names to lowercase for case-insensitive matching
    colnames(data) <- tolower(colnames(data))
    
    # Check if we have the minimum required columns
    if (nrow(data) == 0) {
        warning(paste("Empty file:", filepath))
        return(NULL)
    }
    
    if (fileType == "intended") {
        # Intended path format: point_index,x,y,z
        # Try to find x and z columns (case-insensitive)
        if (!("x" %in% colnames(data)) || !("z" %in% colnames(data))) {
            warning(paste("Missing x or z column in intended file:", filepath, 
                         "\nAvailable columns:", paste(colnames(data), collapse = ", ")))
            return(NULL)
        }
        
        return(data.frame(
            x = as.numeric(data$x),
            y = ifelse("y" %in% colnames(data), as.numeric(data$y), 0),
            z = as.numeric(data$z),
            point_index = ifelse("point_index" %in% colnames(data), data$point_index, 1:nrow(data)),
            stringsAsFactors = FALSE
        ))
    } else if (fileType == "actual") {
        # Actual path format: time,x,y,z,vx,vy,vz
        # Try to find x and z columns (case-insensitive)
        if (!("x" %in% colnames(data)) || !("z" %in% colnames(data))) {
            warning(paste("Missing x or z column in actual file:", filepath,
                         "\nAvailable columns:", paste(colnames(data), collapse = ", ")))
            return(NULL)
        }
        
        return(data.frame(
            x = as.numeric(data$x),
            y = ifelse("y" %in% colnames(data), as.numeric(data$y), 0),
            z = as.numeric(data$z),
            time = ifelse("time" %in% colnames(data), as.numeric(data$time), 1:nrow(data)),
            stringsAsFactors = FALSE
        ))
    }
    
    warning(paste("Unexpected file type:", fileType))
    return(NULL)
}

# ============================================================================
# Main Visualization Function
# ============================================================================

visualizePaths <- function(folder = experimentFolder, 
                          patternId = NULL, 
                          phase = NULL,
                          savePlots = TRUE) {
    
    if (!dir.exists(folder)) {
        stop(paste("Experiment folder not found:", folder))
    }
    
    cat("Scanning folder:", folder, "\n")
    
    # Find all path files
    fileInfo <- findPathFiles(folder)
    
    if (nrow(fileInfo) == 0) {
        stop("No path CSV files found in the folder")
    }
    
    # Debug: Show what files were found
    cat("\n=== DEBUG: Files Found ===\n")
    cat("Total files:", nrow(fileInfo), "\n")
    cat("\nFile breakdown:\n")
    print(fileInfo[, c("filename", "type", "phase", "patternId")])
    cat("\nUnique pattern IDs:", paste(unique(fileInfo$patternId), collapse = ", "), "\n")
    cat("Unique phases:", paste(unique(fileInfo$phase), collapse = ", "), "\n")
    cat("===========================\n\n")
    
    # Filter by pattern and phase if specified
    if (!is.null(patternId)) {
        fileInfo <- fileInfo[fileInfo$patternId == patternId, ]
    }
    if (!is.null(phase)) {
        fileInfo <- fileInfo[fileInfo$phase == phase, ]
    }
    
    if (nrow(fileInfo) == 0) {
        stop("No files match the specified pattern/phase")
    }
    
    # Get unique pattern-phase combinations
    combinations <- unique(fileInfo[, c("patternId", "phase")])
    
    cat("Found", nrow(combinations), "pattern-phase combination(s)\n")
    
    plots <- list()
    
    # Create plot for each combination
    for (i in 1:nrow(combinations)) {
        patId <- combinations$patternId[i]
        ph <- combinations$phase[i]
        
        cat("Processing:", patId, "-", ph, "\n")
        
        # Find matching files
        intendedFile <- fileInfo$filename[fileInfo$type == "intended" & 
                                          fileInfo$patternId == patId & 
                                          fileInfo$phase == ph]
        actualFile <- fileInfo$filename[fileInfo$type == "actual" & 
                                       fileInfo$patternId == patId & 
                                       fileInfo$phase == ph]
        
        # Check what we have
        cat("  Intended files found:", length(intendedFile), "\n")
        cat("  Actual files found:", length(actualFile), "\n")
        
        if (length(intendedFile) == 0 && length(actualFile) == 0) {
            warning(paste("No files found for", patId, ph, "- skipping"))
            next
        }
        
        # If we're missing one type, warn but continue with what we have
        if (length(intendedFile) == 0) {
            warning(paste("No intended path file for", patId, ph, "- plotting actual path only"))
        }
        if (length(actualFile) == 0) {
            warning(paste("No actual path file for", patId, ph, "- plotting intended path only"))
        }
        
        # Use the most recent file if multiple exist
        if (length(intendedFile) > 1) {
            intendedFile <- intendedFile[which.max(file.info(intendedFile)$mtime)]
        }
        if (length(actualFile) > 1) {
            actualFile <- actualFile[which.max(file.info(actualFile)$mtime)]
        }
        
        # Load data (handle missing files)
        intendedData <- NULL
        if (length(intendedFile) > 0) {
            intendedData <- loadPathFile(intendedFile[1], "intended")
        }
        
        actualData <- NULL
        if (length(actualFile) > 0) {
            actualData <- loadPathFile(actualFile[1], "actual")
        }
        
        # Need at least one dataset
        if (is.null(intendedData) && is.null(actualData)) {
            warning(paste("Failed to load any data for", patId, ph, "- skipping"))
            next
        }
        
        # Verify required columns exist for available data
        requiredCols <- c("x", "z")
        if (!is.null(intendedData) && !all(requiredCols %in% colnames(intendedData))) {
            warning(paste("Intended data missing required columns for", patId, ph, 
                         "\nAvailable:", paste(colnames(intendedData), collapse = ", ")))
            intendedData <- NULL  # Don't plot it
        }
        if (!is.null(actualData) && !all(requiredCols %in% colnames(actualData))) {
            warning(paste("Actual data missing required columns for", patId, ph,
                         "\nAvailable:", paste(colnames(actualData), collapse = ", ")))
            actualData <- NULL  # Don't plot it
        }
        
        if (is.null(intendedData) && is.null(actualData)) {
            warning(paste("No valid data to plot for", patId, ph, "- skipping"))
            next
        }
        
        # Create visualization
        p <- ggplot()
        
        # Add intended path if available
        if (!is.null(intendedData)) {
            p <- p +
                geom_path(data = intendedData, aes(x = x, y = z), 
                         color = "blue", linewidth = 1.5, alpha = 0.7, linetype = "solid")
        }
        
        # Add actual path if available
        if (!is.null(actualData)) {
            p <- p +
                geom_path(data = actualData, aes(x = x, y = z), 
                         color = "red", linewidth = 1, alpha = 0.8)
            # Start and end points
            if (nrow(actualData) > 0) {
                p <- p +
                    geom_point(data = actualData[1, ], aes(x = x, y = z), 
                              color = "green", size = 4, shape = 17)
                if (nrow(actualData) > 1) {
                    p <- p +
                        geom_point(data = actualData[nrow(actualData), ], aes(x = x, y = z), 
                                  color = "orange", size = 4, shape = 17)
                }
            }
        }
        
        # Build subtitle and caption
        subtitle <- if (!is.null(intendedData) && !is.null(actualData)) {
            "Blue = Intended Waypoints | Red = Actual Ball Path"
        } else if (!is.null(intendedData)) {
            "Blue = Intended Waypoints"
        } else {
            "Red = Actual Ball Path"
        }
        
        caption <- paste(
            if (!is.null(intendedData)) paste("Intended waypoints:", nrow(intendedData)) else "",
            if (!is.null(intendedData) && !is.null(actualData)) " | " else "",
            if (!is.null(actualData)) paste("Actual samples:", nrow(actualData)) else ""
        )
        
        p <- p +
            labs(
                title = paste("Path Comparison:", patId, "-", ph),
                subtitle = subtitle,
                x = "X Position",
                y = "Z Position",
                caption = caption
            ) +
            theme_minimal() +
            theme(
                plot.title = element_text(size = 14, face = "bold"),
                plot.subtitle = element_text(size = 10),
                aspect.ratio = 1
            ) +
            coord_fixed()
        
        plots[[paste(patId, ph, sep = "_")]] <- p
        
        # Print plot
        print(p)
        
        # Save plot if requested
        if (savePlots) {
            plotFile <- file.path(folder, paste0("path_comparison_", patId, "_", ph, ".png"))
            ggsave(plotFile, plot = p, width = 10, height = 10, dpi = 300)
            cat("Saved plot to:", plotFile, "\n")
        }
    }
    
    return(plots)
}

# ============================================================================
# Helper: Read recall summary file to get percentages and pass/fail status
# ============================================================================
readRecallSummary <- function(folder = experimentFolder) {
    summaryFile <- file.path(folder, "recall_summary.txt")
    
    if (!file.exists(summaryFile)) {
        cat("Warning: recall_summary.txt not found, cannot add recall percentages to plots\n")
        return(NULL)
    }
    
    lines <- readLines(summaryFile)
    
    # Find the "Individual Pattern Results:" section
    resultsStart <- which(grepl("Individual Pattern Results:", lines))
    if (length(resultsStart) == 0) {
        return(NULL)
    }
    
    # Extract pattern results (lines after "Individual Pattern Results:" until blank line or separator)
    results <- data.frame(patternId = character(), recallPercent = numeric(), 
                         passStatus = character(), stringsAsFactors = FALSE)
    
    i <- resultsStart + 1
    while (i <= length(lines) && !grepl("^════", lines[i]) && lines[i] != "") {
        line <- trimws(lines[i])
        if (line == "") {
            i <- i + 1
            next
        }
        
        # Parse format: "geo_01: 74.6% - PASS" or "geo_01: 74.6% (best: 100.0%, tested 7x) - PASS"
        # Extract pattern ID, percentage, and status
        if (grepl(":", line) && grepl("%", line)) {
            # Extract pattern ID (everything before ":")
            patternId <- trimws(sub(":.*$", "", line))
            
            # Extract percentage (number before "%")
            percentMatch <- regmatches(line, regexpr("\\d+\\.?\\d*%", line))
            if (length(percentMatch) > 0) {
                recallPercent <- as.numeric(sub("%", "", percentMatch[1]))
            } else {
                recallPercent <- NA
            }
            
            # Extract pass/fail status
            if (grepl("\\bPASS\\b", line)) {
                passStatus <- "SUCCESS"
            } else if (grepl("\\bFAIL\\b", line)) {
                passStatus <- "FAIL"
            } else {
                passStatus <- "UNKNOWN"
            }
            
            results <- rbind(results, data.frame(
                patternId = patternId,
                recallPercent = recallPercent,
                passStatus = passStatus,
                stringsAsFactors = FALSE
            ))
        }
        i <- i + 1
    }
    
    return(results)
}

# Helper: List all available patterns
# ============================================================================

listAvailablePatterns <- function(folder = experimentFolder) {
    if (!dir.exists(folder)) {
        stop(paste("Experiment folder not found:", folder))
    }
    
    fileInfo <- findPathFiles(folder)
    
    if (nrow(fileInfo) == 0) {
        cat("No path CSV files found in the folder\n")
        return(NULL)
    }
    
    # Get unique pattern-phase combinations
    combinations <- unique(fileInfo[, c("patternId", "phase")])
    
    cat("\n=== Available Patterns ===\n")
    for (i in 1:nrow(combinations)) {
        patId <- combinations$patternId[i]
        ph <- combinations$phase[i]
        
        intendedCount <- sum(fileInfo$type == "intended" & 
                            fileInfo$patternId == patId & 
                            fileInfo$phase == ph)
        actualCount <- sum(fileInfo$type == "actual" & 
                          fileInfo$patternId == patId & 
                          fileInfo$phase == ph)
        
        cat(sprintf("%d. Pattern: %s | Phase: %s | Intended: %d | Actual: %d\n", 
                   i, patId, ph, intendedCount, actualCount))
    }
    cat("==========================\n\n")
    
    return(combinations)
}

# ============================================================================
# Batch Visualization: Plot all patterns
# ============================================================================

visualizeAllPaths <- function(folder = experimentFolder, savePlots = TRUE) {
    # First, list what's available
    combinations <- listAvailablePatterns(folder)
    
    if (is.null(combinations) || nrow(combinations) == 0) {
        stop("No patterns found to visualize")
    }
    
    cat("Generating plots for", nrow(combinations), "pattern-phase combination(s)...\n\n")
    
    # Visualize all patterns
    return(visualizePaths(folder = folder, savePlots = savePlots))
}

# ============================================================================
# Visualize specific patterns individually
# ============================================================================

visualizePatterns <- function(patternIds = NULL, phase = "recall", 
                             folder = experimentFolder, savePlots = TRUE) {
    if (!dir.exists(folder)) {
        stop(paste("Experiment folder not found:", folder))
    }
    
    # If no patterns specified, get all available
    if (is.null(patternIds)) {
        combinations <- listAvailablePatterns(folder)
        if (is.null(combinations)) {
            stop("No patterns found")
        }
        # Filter by phase if specified
        if (!is.null(phase)) {
            combinations <- combinations[combinations$phase == phase, ]
        }
        patternIds <- unique(combinations$patternId)
    }
    
    cat("Visualizing", length(patternIds), "pattern(s):", paste(patternIds, collapse = ", "), "\n\n")
    
    allPlots <- list()
    
    for (patId in patternIds) {
        cat("\n--- Processing Pattern:", patId, "---\n")
        plots <- visualizePaths(folder = folder, patternId = patId, 
                               phase = phase, savePlots = savePlots)
        allPlots <- c(allPlots, plots)
    }
    
    return(allPlots)
}

# ============================================================================
# Cumulative Recall Visualization: Triangular Grid Layout
# ============================================================================

# Create a single comparison plot (intended vs actual)
createComparisonPlot <- function(intendedData, actualData, patternId, phase, 
                                title = NULL, showLegend = TRUE,
                                recallPercent = NULL, passStatus = NULL,
                                patternNum = NULL, totalPatterns = NULL,
                                allIntendedPaths = NULL) {
    p <- ggplot()
    
    # Add all intended paths if provided (for cumulative recall visualization)
    # allIntendedPaths should be a list of data frames, each with a 'patternId' attribute
    if (!is.null(allIntendedPaths) && length(allIntendedPaths) > 0) {
        for (i in 1:length(allIntendedPaths)) {
            pathData <- allIntendedPaths[[i]]
            if (!is.null(pathData) && nrow(pathData) > 0) {
                # Calculate alpha: lighter for earlier patterns, brighter for current
                # Current pattern (last in list) gets alpha = 0.8, earlier ones get progressively lighter
                totalPaths <- length(allIntendedPaths)
                isCurrentPattern <- (i == length(allIntendedPaths))
                
                if (isCurrentPattern) {
                    # Current pattern: brighter blue
                    alpha <- 0.8
                    linewidth <- 1.2
                } else {
                    # Earlier patterns: lighter blue
                    alpha <- 0.3 + 0.2 * (i / totalPaths)  # Range from 0.3 to 0.5
                    linewidth <- 0.8
                }
                
                p <- p +
                    geom_path(data = pathData, aes(x = x, y = z), 
                             color = "blue", linewidth = linewidth, 
                             alpha = alpha, linetype = "solid")
            }
        }
    } else if (!is.null(intendedData) && nrow(intendedData) > 0) {
        # Fallback: single intended path if allIntendedPaths not provided
        p <- p +
            geom_path(data = intendedData, aes(x = x, y = z), 
                     color = "blue", linewidth = 1, alpha = 0.7, linetype = "solid")
    }
    
    # Add actual path if available
    if (!is.null(actualData) && nrow(actualData) > 0) {
        p <- p +
            geom_path(data = actualData, aes(x = x, y = z), 
                     color = "red", linewidth = 0.8, alpha = 0.8)
        # Start and end points
        p <- p +
            geom_point(data = actualData[1, ], aes(x = x, y = z), 
                      color = "green", size = 3, shape = 17)
        if (nrow(actualData) > 1) {
            p <- p +
                geom_point(data = actualData[nrow(actualData), ], aes(x = x, y = z), 
                          color = "orange", size = 3, shape = 17)
        }
    }
    
    # Build title with recall info if available
    if (is.null(title)) {
        if (!is.null(recallPercent) && !is.null(passStatus) && 
            !is.null(patternNum) && !is.null(totalPatterns)) {
            # Format: "Pattern X of Y (Z%): SUCCESS." or "Pattern X of Y (Z%): FAIL."
            statusText <- ifelse(passStatus == "SUCCESS", "SUCCESS.", "FAIL.")
            title <- sprintf("Pattern %d of %d (%.1f%%): %s", 
                           patternNum, totalPatterns, recallPercent, statusText)
        } else {
            title <- paste(patternId, "-", phase)
        }
    }
    
    p <- p +
        labs(
            title = title,
            x = "X",
            y = "Z"
        ) +
        theme_minimal() +
        theme(
            plot.title = element_text(size = 10, face = "bold", hjust = 0.5),
            axis.title = element_text(size = 8),
            axis.text = element_text(size = 7),
            plot.margin = margin(5, 5, 5, 5),
            aspect.ratio = 1,
            legend.position = if (showLegend) "none" else "none"
        ) +
        coord_fixed()
    
    return(p)
}

# Find and organize cumulative recall files
organizeCumulativeRecall <- function(folder = experimentFolder) {
    if (!dir.exists(folder)) {
        stop(paste("Experiment folder not found:", folder))
    }
    
    fileInfo <- findPathFiles(folder)
    
    # Filter for recall phase only
    recallFiles <- fileInfo[fileInfo$phase == "recall", ]
    
    if (nrow(recallFiles) == 0) {
        stop("No recall files found")
    }
    
    # Extract pattern numbers and sort
    patternIds <- unique(recallFiles$patternId)
    
    # Sort patterns (handle both geo_XX and pat_XX formats)
    extractPatternNum <- function(patId) {
        # Try to extract number from pattern ID
        numMatch <- regmatches(patId, regexpr("\\d+", patId))
        if (length(numMatch) > 0) {
            return(as.numeric(numMatch[1]))
        }
        return(999)  # Put unknown patterns at end
    }
    
    patternNums <- sapply(patternIds, extractPatternNum)
    sortedIndices <- order(patternNums)
    sortedPatternIds <- patternIds[sortedIndices]
    
    cat("Found", length(sortedPatternIds), "patterns:", paste(sortedPatternIds, collapse = ", "), "\n")
    
    # Organize by cumulative groups
    # After learning pattern 1: test pattern 1
    # After learning pattern 2: test patterns 1, 2
    # After learning pattern 3: test patterns 1, 2, 3
    # etc.
    
    cumulativeGroups <- list()
    
    for (i in 1:length(sortedPatternIds)) {
        # Patterns to test at this stage
        patternsToTest <- sortedPatternIds[1:i]
        groupName <- paste0("after_", sortedPatternIds[i])
        
        cumulativeGroups[[groupName]] <- list(
            patterns = patternsToTest,
            files = list()
        )
        
        # Find files for each pattern in this group
        for (patId in patternsToTest) {
            intendedFile <- recallFiles$filename[recallFiles$type == "intended" & 
                                                recallFiles$patternId == patId]
            actualFile <- recallFiles$filename[recallFiles$type == "actual" & 
                                              recallFiles$patternId == patId]
            
            # Use most recent if multiple
            if (length(intendedFile) > 1) {
                intendedFile <- intendedFile[which.max(file.info(intendedFile)$mtime)]
            }
            if (length(actualFile) > 1) {
                actualFile <- actualFile[which.max(file.info(actualFile)$mtime)]
            }
            
            cumulativeGroups[[groupName]]$files[[patId]] <- list(
                intended = if (length(intendedFile) > 0) intendedFile[1] else NULL,
                actual = if (length(actualFile) > 0) actualFile[1] else NULL
            )
        }
    }
    
    return(list(
        patternIds = sortedPatternIds,
        groups = cumulativeGroups
    ))
}

# Create triangular grid visualization
visualizeCumulativeRecallGrid <- function(folder = experimentFolder, savePlot = TRUE) {
    # Organize files
    organized <- organizeCumulativeRecall(folder)
    
    patternIds <- organized$patternIds
    groups <- organized$groups
    
    # Read recall summary to get percentages and pass/fail status
    recallSummary <- readRecallSummary(folder)
    
    # Helper to extract pattern number from pattern ID
    extractPatternNum <- function(patId) {
        numMatch <- regmatches(patId, regexpr("\\d+", patId))
        if (length(numMatch) > 0) {
            return(as.numeric(numMatch[1]))
        }
        return(999)
    }
    
    cat("\n=== Creating Triangular Grid Layout ===\n")
    cat("Patterns:", paste(patternIds, collapse = ", "), "\n\n")
    
    allPlots <- list()
    totalPatterns <- length(patternIds)
    
    # Create plots for each row of the triangle
    for (i in 1:length(patternIds)) {
        groupName <- names(groups)[i]
        group <- groups[[groupName]]
        
        cat("Row", i, ": Creating", length(group$patterns), "comparison plot(s)\n")
        
        rowPlots <- list()
        
        for (patId in group$patterns) {
            files <- group$files[[patId]]
            
            # Load actual data for this specific pattern
            actualData <- NULL
            if (!is.null(files$actual)) {
                actualData <- loadPathFile(files$actual, "actual")
            }
            
            # Load ALL intended paths for all patterns learned up to this point
            # This shows the interference from overlapping patterns
            allIntendedPaths <- list()
            for (learnedPatId in group$patterns) {
                learnedFiles <- group$files[[learnedPatId]]
                if (!is.null(learnedFiles$intended)) {
                    intendedPathData <- loadPathFile(learnedFiles$intended, "intended")
                    if (!is.null(intendedPathData) && nrow(intendedPathData) > 0) {
                        allIntendedPaths[[length(allIntendedPaths) + 1]] <- intendedPathData
                    }
                }
            }
            
            # Get recall info for this pattern
            recallPercent <- NULL
            passStatus <- NULL
            patternNum <- extractPatternNum(patId)
            
            if (!is.null(recallSummary)) {
                patternRow <- recallSummary[recallSummary$patternId == patId, ]
                if (nrow(patternRow) > 0) {
                    recallPercent <- patternRow$recallPercent[1]
                    passStatus <- patternRow$passStatus[1]
                }
            }
            
            # Create plot with recall info and all intended paths
            plot <- createComparisonPlot(intendedData = NULL, actualData = actualData, 
                                       patternId = patId, phase = "recall", 
                                       title = NULL, showLegend = TRUE,
                                       recallPercent = recallPercent,
                                       passStatus = passStatus,
                                       patternNum = patternNum,
                                       totalPatterns = totalPatterns,
                                       allIntendedPaths = allIntendedPaths)
            rowPlots[[patId]] <- plot
        }
        
        allPlots[[paste0("row_", i)]] <- rowPlots
    }
    
    # Arrange in triangular grid
    cat("\n=== Arranging Plots in Triangular Grid ===\n")
    
    # Create list of grobs for grid.arrange
    plotList <- list()
    
    for (i in 1:length(allPlots)) {
        rowPlots <- allPlots[[paste0("row_", i)]]
        for (j in 1:length(rowPlots)) {
            plotList[[length(plotList) + 1]] <- rowPlots[[j]]
        }
    }
    
    # Calculate grid layout: row i has i plots
    # Total plots = 1 + 2 + 3 + ... + n = n(n+1)/2
    n <- length(patternIds)
    totalPlots <- n * (n + 1) / 2
    
    cat("Total plots:", totalPlots, "\n")
    cat("Grid layout: Row 1 (1 plot), Row 2 (2 plots), ..., Row", n, "(", n, "plots)\n\n")
    
    # Create layout matrix for triangular arrangement
    # We'll use a simple approach: arrange plots in rows
    # Row 1: 1 plot (centered or left-aligned)
    # Row 2: 2 plots
    # Row 3: 3 plots
    # etc.
    
    # For grid.arrange, we need to specify ncol = max plots per row
    # and use layout_matrix to position them
    
    maxCols <- length(patternIds)
    
    # Build layout matrix
    layoutMatrix <- matrix(NA, nrow = length(patternIds), ncol = maxCols)
    plotIndex <- 1
    
    for (i in 1:length(patternIds)) {
        rowPlots <- allPlots[[paste0("row_", i)]]
        for (j in 1:length(rowPlots)) {
            layoutMatrix[i, j] <- plotIndex
            plotIndex <- plotIndex + 1
        }
    }
    
    # Create the grid
    gridPlot <- do.call(grid.arrange, c(plotList, list(
        layout_matrix = layoutMatrix,
        ncol = maxCols,
        top = textGrob("Cumulative Recall: Intended vs Actual Paths", 
                      gp = gpar(fontsize = 16, fontface = "bold"))
    )))
    
    # Print
    print(gridPlot)
    
    # Save
    if (savePlot) {
        plotFile <- file.path(folder, "cumulative_recall_triangular_grid.png")
        png(plotFile, width = maxCols * 4, height = length(patternIds) * 4, 
            units = "in", res = 300)
        do.call(grid.arrange, c(plotList, list(
            layout_matrix = layoutMatrix,
            ncol = maxCols,
            top = textGrob("Cumulative Recall: Intended vs Actual Paths", 
                          gp = gpar(fontsize = 16, fontface = "bold"))
        )))
        dev.off()
        cat("\nSaved triangular grid to:", plotFile, "\n")
    }
    
    return(list(plots = allPlots, gridPlot = gridPlot))
}

# ============================================================================
# Forgetting Curves: Track recall degradation as patterns are added
# ============================================================================

# Calculate recall percentage from path data
# Compares actual path points to intended path waypoints
calculateRecallFromPaths <- function(actualData, intendedData, radiusThreshold = 2.0, debug = FALSE) {
    if (is.null(actualData) || nrow(actualData) == 0 || 
        is.null(intendedData) || nrow(intendedData) == 0) {
        if (debug) cat("    calculateRecallFromPaths: Missing data\n")
        return(NA)
    }
    
    # Sample actual path at regular intervals (similar to Unity's recallSampleInterval)
    # Use all points or sample every Nth point to get ~200 samples
    sampleInterval <- max(1, floor(nrow(actualData) / 200))
    sampledActual <- actualData[seq(1, nrow(actualData), by = sampleInterval), ]
    
    inRangeCount <- 0
    totalSamples <- nrow(sampledActual)
    distances <- numeric(totalSamples)
    
    if (totalSamples == 0) {
        if (debug) cat("    calculateRecallFromPaths: No samples\n")
        return(NA)
    }
    
    # Interpolate intended path to create a dense path for comparison
    # Create interpolated points along the intended waypoint path
    # This better matches Unity's calculation which checks distance to magnet position along path
    if (nrow(intendedData) < 2) {
        # Fallback to waypoint comparison if only one waypoint
        for (i in 1:totalSamples) {
            actualPoint <- c(sampledActual$x[i], sampledActual$z[i])
            intendedPoint <- c(intendedData$x[1], intendedData$z[1])
            minDist <- sqrt(sum((actualPoint - intendedPoint)^2))
            distances[i] <- minDist
            if (minDist <= radiusThreshold) {
                inRangeCount <- inRangeCount + 1
            }
        }
    } else {
        # Interpolate along waypoint segments
        # For each actual point, find distance to nearest point on the intended path
        for (i in 1:totalSamples) {
            actualPoint <- c(sampledActual$x[i], sampledActual$z[i])
            
            # Find minimum distance to any point on the intended path (interpolated between waypoints)
            minDist <- Inf
            
            # Check distance to each waypoint
            for (j in 1:nrow(intendedData)) {
                intendedPoint <- c(intendedData$x[j], intendedData$z[j])
                dist <- sqrt(sum((actualPoint - intendedPoint)^2))
                if (dist < minDist) {
                    minDist <- dist
                }
            }
            
            # Also check distance to interpolated points between waypoints
            # This is more accurate for curved paths
            for (j in 1:(nrow(intendedData) - 1)) {
                p1 <- c(intendedData$x[j], intendedData$z[j])
                p2 <- c(intendedData$x[j + 1], intendedData$z[j + 1])
                
                # Project actual point onto line segment p1-p2
                v <- p2 - p1
                w <- actualPoint - p1
                
                # Calculate dot product and vector length squared
                dotProduct <- sum(w * v)
                vLengthSq <- sum(v * v)
                
                if (vLengthSq > 1e-10) {  # Avoid division by zero
                    # Clamp t to [0, 1] to stay on segment
                    t <- max(0, min(1, dotProduct / vLengthSq))
                    closestPoint <- p1 + t * v
                    
                    dist <- sqrt(sum((actualPoint - closestPoint)^2))
                    if (dist < minDist) {
                        minDist <- dist
                    }
                }
            }
            
            distances[i] <- minDist
            
            # Check if within threshold
            if (minDist <= radiusThreshold) {
                inRangeCount <- inRangeCount + 1
            }
        }
    }
    
    recallPercent <- 100.0 * inRangeCount / totalSamples
    
    if (debug) {
        cat("    calculateRecallFromPaths: ", totalSamples, " samples, ", inRangeCount, " in range (threshold: ", 
            radiusThreshold, "), recall: ", sprintf("%.1f%%", recallPercent), 
            " (distances: min=", sprintf("%.2f", min(distances)), 
            ", max=", sprintf("%.2f", max(distances)),
            ", mean=", sprintf("%.2f", mean(distances)), ")\n", sep = "")
    }
    
    return(recallPercent)
}

# Extract forgetting curve data from cumulative recall stages
# This finds ALL recall test files for each pattern and tracks recall across all stages
extractForgettingCurveData <- function(folder = experimentFolder, 
                                       radiusThreshold = 2.0) {
    if (!dir.exists(folder)) {
        stop(paste("Experiment folder not found:", folder))
    }
    
    cat("\n=== Extracting Forgetting Curve Data ===\n")
    
    # Find all path files
    fileInfo <- findPathFiles(folder)
    
    # Filter for recall phase only (actual paths - these are the recall tests)
    recallFiles <- fileInfo[fileInfo$phase == "recall" & fileInfo$type == "actual", ]
    
    if (nrow(recallFiles) == 0) {
        stop("No recall test files found. Make sure cumulative recall mode was used.")
    }
    
    # Get all unique patterns
    patternIds <- unique(recallFiles$patternId)
    patternIds <- patternIds[!is.na(patternIds) & patternIds != ""]
    
    # Sort patterns by number
    extractPatternNum <- function(patId) {
        numMatch <- regmatches(patId, regexpr("\\d+", patId))
        if (length(numMatch) > 0) {
            return(as.numeric(numMatch[1]))
        }
        return(999)
    }
    
    patternNums <- sapply(patternIds, extractPatternNum)
    sortedIndices <- order(patternNums)
    sortedPatternIds <- patternIds[sortedIndices]
    
    cat("Found", length(sortedPatternIds), "patterns:", paste(sortedPatternIds, collapse = ", "), "\n")
    cat("Found", nrow(recallFiles), "recall test files\n")
    cat("Using recall radius threshold:", radiusThreshold, "\n\n")
    
    # Sort recall files by modification time to determine test order
    recallFiles$mtime <- file.info(recallFiles$filename)$mtime
    recallFiles <- recallFiles[order(recallFiles$mtime), ]
    
    # For each pattern, find all its recall test files and determine which stage each belongs to
    # In cumulative mode: after learning pattern N, patterns 1..N are all tested
    # So we need to figure out which test belongs to which stage
    
    # Strategy: Group recall tests by their order in time
    # The first set of tests (after learning pattern 1) tests pattern 1
    # The second set (after learning pattern 2) tests patterns 1 and 2
    # etc.
    
    # Find all intended path files for reference
    intendedFiles <- fileInfo[fileInfo$phase == "recall" & fileInfo$type == "intended", ]
    
    # Create data frame to store results
    forgettingData <- data.frame(
        patternId = character(),
        stage = integer(),
        recallPercent = numeric(),
        patternsLearned = integer(),
        testNumber = integer(),  # Which test this is for this pattern
        stringsAsFactors = FALSE
    )
    
    # Track stages correctly for cumulative recall
    # In cumulative recall, tests come in batches:
    # Stage 1: test pattern 1 (1 test) - pattern 1's test count = 1
    # Stage 2: test pattern 1, then pattern 2 (2 tests) - pattern 1's test count = 2, pattern 2's = 1
    # Stage 3: test pattern 1, then pattern 2, then pattern 3 (3 tests) - pattern 1's = 3, pattern 2's = 2, pattern 3's = 1
    # 
    # Key insight: For pattern N, its test count equals the stage number when it's tested.
    # So pattern 1's 2nd test happens at stage 2, pattern 1's 3rd test happens at stage 3, etc.
    # For a pattern's first test, the stage equals its pattern number.
    
    patternTestCounts <- setNames(rep(0, length(sortedPatternIds)), sortedPatternIds)
    
    cat("Processing recall tests in chronological order...\n\n")
    
    # Process each recall test file in order
    for (i in 1:nrow(recallFiles)) {
        recallFile <- recallFiles[i, ]
        patId <- recallFile$patternId
        
        # Find corresponding intended path file
        # In cumulative recall, the intended path should be the same for all tests of a pattern
        # But we should match by pattern ID, not by timestamp
        intendedFile <- intendedFiles[intendedFiles$patternId == patId, ]
        if (nrow(intendedFile) > 0) {
            # Use any intended file for this pattern (they should all be the same)
            # Prefer the first one found, or the one with the pattern ID in the filename
            intendedPath <- intendedFile$filename[1]
            cat("    Using intended path:", basename(intendedPath), "\n")
        } else {
            intendedPath <- NULL
            cat("    WARNING: No intended path found for", patId, "\n")
        }
        
        # Extract pattern number
        patNum <- extractPatternNum(patId)
        
        # Increment test count for this pattern BEFORE determining stage
        patternTestCounts[patId] <- patternTestCounts[patId] + 1
        testNum <- patternTestCounts[patId]
        
        # Determine stage based on pattern number and test count
        # Pattern 1: test 1 = stage 1, test 2 = stage 2, test 3 = stage 3, etc. (stage = testNum)
        # Pattern 2: test 1 = stage 2, test 2 = stage 3, test 3 = stage 4, etc. (stage = testNum + 1)
        # Pattern 3: test 1 = stage 3, test 2 = stage 4, test 3 = stage 5, etc. (stage = testNum + 2)
        # General formula: stage = testNum + (patNum - 1)
        currentStage <- testNum + (patNum - 1)
        
        if (testNum == 1) {
            cat("Stage", currentStage, ": First test of", patId, "\n")
        }
        
        # Load paths
        actualData <- loadPathFile(recallFile$filename, "actual")
        intendedData <- if (!is.null(intendedPath)) loadPathFile(intendedPath, "intended") else NULL
        
        # Calculate recall percentage
        # Debug first few tests to see what's happening
        # Always debug to see actual distances being calculated
        recallPercent <- calculateRecallFromPaths(actualData, intendedData, radiusThreshold, debug = TRUE)
        
        if (!is.na(recallPercent)) {
            forgettingData <- rbind(forgettingData, data.frame(
                patternId = patId,
                stage = currentStage,
                recallPercent = recallPercent,
                patternsLearned = currentStage,
                testNumber = testNum,
                stringsAsFactors = FALSE
            ))
            cat("  Test", testNum, "-", patId, "at stage", currentStage, ": ", 
                sprintf("%.1f%%", recallPercent), 
                " (actual points:", if(!is.null(actualData)) nrow(actualData) else 0,
                ", intended points:", if(!is.null(intendedData)) nrow(intendedData) else 0, ")\n", sep = "")
        } else {
            cat("  Test", testNum, "-", patId, "at stage", currentStage, ": (no data - actual:", 
                if(!is.null(actualData)) nrow(actualData) else "NULL",
                ", intended:", if(!is.null(intendedData)) nrow(intendedData) else "NULL", ")\n", sep = "")
        }
    }
    
    cat("\n=== Summary ===\n")
    cat("Total data points:", nrow(forgettingData), "\n")
    cat("\nTests per pattern:\n")
    for (patId in sortedPatternIds) {
        patternData <- forgettingData[forgettingData$patternId == patId, ]
        if (nrow(patternData) > 0) {
            cat("  ", patId, ": ", nrow(patternData), " test(s) across stages ", 
                min(patternData$stage), "-", max(patternData$stage), 
                " (recall range: ", sprintf("%.1f", min(patternData$recallPercent)), 
                "%-", sprintf("%.1f", max(patternData$recallPercent)), "%)\n", sep = "")
            # Show detailed breakdown
            for (j in 1:nrow(patternData)) {
                cat("    Stage ", patternData$stage[j], ": ", 
                    sprintf("%.1f%%", patternData$recallPercent[j]), "\n", sep = "")
            }
        }
    }
    
    return(forgettingData)
}

# Visualize forgetting curves
visualizeForgettingCurves <- function(folder = experimentFolder, 
                                     radiusThreshold = 2.0,
                                     savePlot = TRUE,
                                     showPassThreshold = NULL) {
    # Extract data
    forgettingData <- extractForgettingCurveData(folder, radiusThreshold)
    
    if (nrow(forgettingData) == 0) {
        stop("No forgetting curve data found. Make sure cumulative recall mode was used.")
    }
    
    cat("\n=== Creating Forgetting Curves Plot ===\n")
    
    # Get unique patterns
    uniquePatterns <- unique(forgettingData$patternId)
    
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
    
    # Show data summary for each pattern
    cat("\nData points per pattern:\n")
    for (patId in sortedPatterns) {
        patternData <- forgettingData[forgettingData$patternId == patId, ]
        stages <- sort(unique(patternData$stage))
        cat("  ", patId, ": ", nrow(patternData), " test(s) at stages ", 
            paste(stages, collapse = ", "), "\n", sep = "")
    }
    
    # Create color palette
    nPatterns <- length(sortedPatterns)
    if (nPatterns <= 10) {
        if (!require("RColorBrewer", quietly = TRUE)) {
            install.packages("RColorBrewer")
            library(RColorBrewer)
        }
        colors <- RColorBrewer::brewer.pal(max(3, nPatterns), "Set1")
        if (nPatterns < length(colors)) {
            colors <- colors[1:nPatterns]
        }
    } else {
        colors <- rainbow(nPatterns)
    }
    
    # Create the plot
    p <- ggplot(forgettingData, aes(x = stage, y = recallPercent, color = patternId)) +
        geom_line(linewidth = 1.2, alpha = 0.8) +
        geom_point(size = 2.5, alpha = 0.9) +
        scale_color_manual(values = colors, name = "Pattern") +
        labs(
            title = "Forgetting Curves: Recall Performance as Patterns Are Added",
            subtitle = "Each line shows how a pattern's recall degrades as more patterns are learned",
            x = "Number of Patterns Learned",
            y = "Recall Percentage (%)",
            color = "Pattern ID"
        ) +
        theme_minimal() +
        theme(
            plot.title = element_text(size = 14, face = "bold", hjust = 0.5),
            plot.subtitle = element_text(size = 11, hjust = 0.5, margin = margin(b = 15)),
            axis.title = element_text(size = 12),
            axis.text = element_text(size = 10),
            legend.title = element_text(size = 11, face = "bold"),
            legend.text = element_text(size = 10),
            legend.position = "right",
            panel.grid.minor = element_blank(),
            panel.grid.major = element_line(color = "gray90", linewidth = 0.5)
        ) +
        scale_x_continuous(breaks = unique(forgettingData$stage), minor_breaks = NULL) +
        scale_y_continuous(limits = c(0, 100), breaks = seq(0, 100, 20))
    
    # Add pass threshold line if provided
    if (!is.null(showPassThreshold) && is.numeric(showPassThreshold)) {
        p <- p +
            geom_hline(yintercept = showPassThreshold, linetype = "dashed", 
                      color = "gray50", linewidth = 0.8, alpha = 0.7) +
            annotate("text", x = max(forgettingData$stage), y = showPassThreshold + 2,
                    label = paste("Pass Threshold:", showPassThreshold, "%"),
                    hjust = 1, vjust = 0, size = 3.5, color = "gray40")
    }
    
    # Print plot
    print(p)
    
    # Save plot
    if (savePlot) {
        plotFile <- file.path(folder, "forgetting_curves.png")
        ggsave(plotFile, plot = p, width = 12, height = 7, dpi = 300)
        cat("\nSaved forgetting curves plot to:", plotFile, "\n")
    }
    
    # Also create a summary table
    cat("\n=== Forgetting Curve Summary ===\n")
    summaryTable <- forgettingData %>%
        group_by(patternId) %>%
        summarise(
            FirstRecall = first(recallPercent),
            LastRecall = last(recallPercent),
            BestRecall = max(recallPercent),
            WorstRecall = min(recallPercent),
            Decline = first(recallPercent) - last(recallPercent),
            .groups = "drop"
        ) %>%
        arrange(extractPatternNum(patternId))
    
    print(summaryTable)
    
    # Save summary table
    if (savePlot) {
        summaryFile <- file.path(folder, "forgetting_curve_summary.csv")
        write.csv(summaryTable, summaryFile, row.names = FALSE)
        cat("\nSaved summary table to:", summaryFile, "\n")
    }
    
    return(list(plot = p, data = forgettingData, summary = summaryTable))
}

# ============================================================================
# Quick Start: Uncomment and modify the path below
# ============================================================================

# # Set your experiment folder path (for seed 27)
# experimentFolder <- "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/_seed27"
# 
# # OPTION 1: Create triangular grid for cumulative recall
# result <- visualizeCumulativeRecallGrid(experimentFolder)
# 
# # OPTION 2: Visualize all paths individually
# plots <- visualizeAllPaths(experimentFolder)
# 
# # OPTION 3: Visualize a specific pattern
# # plots <- visualizePaths(experimentFolder, patternId = "geo_01", phase = "recall")
# 
# # OPTION 4: List available patterns first
# # listAvailablePatterns(experimentFolder)
# 
# # OPTION 5: Create forgetting curves (shows recall degradation over time)
# # result <- visualizeForgettingCurves(experimentFolder, 
# #                                    radiusThreshold = 2.0,  # Match your experiment setting
# #                                    showPassThreshold = 80)  # Optional: show pass/fail line