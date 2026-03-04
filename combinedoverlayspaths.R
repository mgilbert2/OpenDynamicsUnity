# ============================================================================
# Combined Recall Overlays
# ============================================================================
# This script visualizes the ACTUAL path taken during recall tests versus
# the INTENDED route from training, arranged in a cumulative grid showing
# progression as new patterns are added.
#
# Usage:
#   # Set working directory to where this script is located
#   setwd("C:/Users/Mak/Attractors")
#   
#   # Source the script
#   source("combinedoverlayspaths.R")
#   
#   # Run the visualization
#   result <- visualizeRecallOverlays(
#       folder = "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/_seed300",
#       passThreshold = 80.0
#   )
# ============================================================================

library(ggplot2)
library(gridExtra)
library(dplyr)

# ============================================================================
# Helper Functions
# ============================================================================

# Read recall history CSV
readRecallHistory <- function(folder) {
  historyFile <- file.path(folder, "recall_history.csv")
  if (file.exists(historyFile)) {
    data <- read.csv(historyFile, stringsAsFactors = FALSE)
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

# Extract pattern number for ordering
extractPatternNum <- function(patId) {
  numMatch <- regmatches(patId, regexpr("\\d+", patId))
  if (length(numMatch) > 0) {
    return(as.numeric(numMatch[1]))
  }
  return(999)
}

# Find path CSV files
findPathFiles <- function(folder) {
  files <- list.files(folder, pattern = "\\.csv$", full.names = TRUE)
  
  # Separate intended and actual paths
  intendedFiles <- files[grepl("_intended_", files, ignore.case = TRUE)]
  actualFiles <- files[!grepl("_intended_", files, ignore.case = TRUE) & 
                         !grepl("experiment_summary", files, ignore.case = TRUE) & 
                         !grepl("recall_summary", files, ignore.case = TRUE) &
                         !grepl("recall_history", files, ignore.case = TRUE) &
                         !grepl("overall_accuracy", files, ignore.case = TRUE)]
  
  # Extract pattern IDs from filenames
  extractPatternId <- function(filename) {
    basename <- basename(filename)
    # Handle patterns like: recall_test_01_*, train_test_01_*, recall_geo_01_*
    if (grepl("test_", basename, ignore.case = TRUE)) {
      match <- regmatches(basename, regexpr("test_\\d+", basename, ignore.case = TRUE))
      if (length(match) > 0) {
        return(tolower(match[1]))
      }
    }
    if (grepl("geo_", basename, ignore.case = TRUE)) {
      match <- regmatches(basename, regexpr("geo_\\d+", basename, ignore.case = TRUE))
      if (length(match) > 0) {
        return(tolower(match[1]))
      }
    }
    if (grepl("pat", basename, ignore.case = TRUE)) {
      match <- regmatches(basename, regexpr("pat\\d+", basename, ignore.case = TRUE))
      if (length(match) > 0) {
        return(tolower(match[1]))
      }
    }
    return(NA)
  }
  
  # Create data frames
  intendedInfo <- data.frame(
    filename = intendedFiles,
    patternId = sapply(intendedFiles, extractPatternId),
    stringsAsFactors = FALSE
  )
  intendedInfo <- intendedInfo[!is.na(intendedInfo$patternId), ]
  
  actualInfo <- data.frame(
    filename = actualFiles,
    patternId = sapply(actualFiles, extractPatternId),
    stringsAsFactors = FALSE
  )
  actualInfo <- actualInfo[!is.na(actualInfo$patternId), ]
  
  # Add timestamp for sorting
  actualInfo$timestamp <- file.mtime(actualInfo$filename)
  
  return(list(intended = intendedInfo, actual = actualInfo))
}

# Load path CSV file
loadPathFile <- function(filepath) {
  if (!file.exists(filepath)) {
    return(NULL)
  }
  
  data <- read.csv(filepath, stringsAsFactors = FALSE)
  colnames(data) <- tolower(colnames(data))
  
  if (nrow(data) == 0) {
    return(NULL)
  }
  
  if (!("x" %in% colnames(data)) || !("z" %in% colnames(data))) {
    return(NULL)
  }
  
  return(data.frame(
    x = as.numeric(data$x),
    z = as.numeric(data$z),
    stringsAsFactors = FALSE
  ))
}

# Calculate global test number across all patterns
calculateGlobalTestNumber <- function(recallHistory) {
  recallHistory <- recallHistory[order(recallHistory$stage, recallHistory$testNumber), ]
  recallHistory$globalTestNumber <- 1:nrow(recallHistory)
  return(recallHistory)
}

# Get the new pattern added at a given stage
getNewPatternAtStage <- function(recallHistory, stage) {
  stageData <- recallHistory[recallHistory$stage == stage, ]
  if (nrow(stageData) == 0) return(NA)
  # Find pattern with testNumber == 1 at this stage (newly added)
  newPattern <- stageData[stageData$testNumber == 1, ]
  if (nrow(newPattern) > 0) {
    return(newPattern$patternId[1])
  }
  return(NA)
}

# ============================================================================
# Main Function
# ============================================================================

visualizeRecallOverlays <- function(folder, passThreshold = 80.0, savePlot = TRUE) {
  
  cat("============================================================================\n")
  cat("Combined Recall Overlays: Intended vs Actual Paths\n")
  cat("============================================================================\n")
  cat("Folder:", folder, "\n")
  cat("Pass Threshold:", passThreshold, "%\n\n")
  
  # Read recall history
  recallHistory <- readRecallHistory(folder)
  if (is.null(recallHistory) || nrow(recallHistory) == 0) {
    stop("recall_history.csv not found or empty.")
  }
  
  cat("Loaded", nrow(recallHistory), "recall test results\n")
  
  # Calculate global test numbers
  recallHistory <- calculateGlobalTestNumber(recallHistory)
  
  # Find path files
  pathFiles <- findPathFiles(folder)
  if (nrow(pathFiles$intended) == 0 || nrow(pathFiles$actual) == 0) {
    stop("No path CSV files found in folder.")
  }
  
  cat("Found", nrow(pathFiles$intended), "intended path files\n")
  cat("Found", nrow(pathFiles$actual), "actual path files\n\n")
  
  # First pass: Calculate global coordinate limits
  cat("=== First Pass: Calculating Global Coordinate Limits ===\n")
  allX <- numeric()
  allZ <- numeric()
  
  for (i in 1:nrow(recallHistory)) {
    test <- recallHistory[i, ]
    patId <- test$patternId
    
    # Find actual path file (most recent for this pattern)
    actualFiles <- pathFiles$actual[pathFiles$actual$patternId == patId, ]
    if (nrow(actualFiles) > 0) {
      actualFiles <- actualFiles[order(actualFiles$timestamp, decreasing = TRUE), ]
      actualData <- loadPathFile(actualFiles$filename[1])
      if (!is.null(actualData)) {
        allX <- c(allX, actualData$x)
        allZ <- c(allZ, actualData$z)
      }
    }
    
    # Find intended path file
    intendedFiles <- pathFiles$intended[pathFiles$intended$patternId == patId, ]
    if (nrow(intendedFiles) > 0) {
      intendedData <- loadPathFile(intendedFiles$filename[1])
      if (!is.null(intendedData)) {
        allX <- c(allX, intendedData$x)
        allZ <- c(allZ, intendedData$z)
      }
    }
  }
  
  if (length(allX) == 0 || length(allZ) == 0) {
    stop("No valid path data found.")
  }
  
  # Calculate limits with padding
  xRange <- range(allX, na.rm = TRUE)
  zRange <- range(allZ, na.rm = TRUE)
  xPadding <- (xRange[2] - xRange[1]) * 0.1
  zPadding <- (zRange[2] - zRange[1]) * 0.1
  
  globalXLim <- c(xRange[1] - xPadding, xRange[2] + xPadding)
  globalZLim <- c(zRange[1] - zPadding, zRange[2] + zPadding)
  
  cat("Global X limits:", globalXLim, "\n")
  cat("Global Z limits:", globalZLim, "\n\n")
  
  # Second pass: Create plots
  cat("=== Second Pass: Creating Overlay Plots ===\n")
  
  # Suppress individual plot output
  oldOptions <- options()
  while (dev.cur() != 1) {
    tryCatch(dev.off(), error = function(e) NULL)
  }
  pdf(file = NULL)
  
  allPlots <- list()
  
  # Track which actual files we've used for each pattern (to match test numbers)
  usedActualFiles <- list()
  
  # Process each recall test in order
  for (i in 1:nrow(recallHistory)) {
    test <- recallHistory[i, ]
    patId <- test$patternId
    stage <- test$stage
    testNum <- test$testNumber
    globalTestNum <- test$globalTestNumber
    recallPercent <- test$recallPercent
    passed <- recallPercent >= passThreshold
    
    # Get new pattern at this stage
    newPattern <- getNewPatternAtStage(recallHistory, stage)
    
    # Find actual path file for this specific test
    # Match by pattern and test number (use testNum-th file for this pattern)
    actualFiles <- pathFiles$actual[pathFiles$actual$patternId == patId, ]
    if (nrow(actualFiles) == 0) {
      cat("  Test", globalTestNum, ": No actual path file for", patId, "\n")
      next
    }
    # Sort by timestamp (oldest first) to match chronological test order
    actualFiles <- actualFiles[order(actualFiles$timestamp), ]
    
    # Use the testNum-th file for this pattern (1st test = 1st file, 2nd test = 2nd file, etc.)
    if (testNum > nrow(actualFiles)) {
      # If we don't have enough files, use the most recent one
      actualData <- loadPathFile(actualFiles$filename[nrow(actualFiles)])
    } else {
      actualData <- loadPathFile(actualFiles$filename[testNum])
    }
    
    # Find intended path file
    intendedFiles <- pathFiles$intended[pathFiles$intended$patternId == patId, ]
    if (nrow(intendedFiles) == 0) {
      cat("  Test", globalTestNum, ": No intended path file for", patId, "\n")
      next
    }
    intendedData <- loadPathFile(intendedFiles$filename[1])
    
    if (is.null(actualData) || is.null(intendedData)) {
      cat("  Test", globalTestNum, ": Could not load paths for", patId, "\n")
      next
    }
    
    # Find all OTHER patterns (all patterns except the current one) to show interference
    # Get all unique pattern IDs from BOTH recall history AND intended path files
    # This ensures we show all learned patterns, not just tested ones
    recallPatternIds <- unique(recallHistory$patternId)
    filePatternIds <- unique(pathFiles$intended$patternId)
    allPatternIds <- unique(c(recallPatternIds, filePatternIds))
    # Remove the current pattern (case-insensitive comparison)
    otherPatternIds <- allPatternIds[tolower(allPatternIds) != tolower(patId)]
    
    # Load intended paths for all other patterns
    otherIntendedPaths <- list()
    if (length(otherPatternIds) > 0) {
      for (otherPatId in otherPatternIds) {
        # Try case-insensitive matching
        otherIntendedFiles <- pathFiles$intended[
          tolower(pathFiles$intended$patternId) == tolower(otherPatId), 
        ]
        if (nrow(otherIntendedFiles) > 0) {
          otherIntendedData <- loadPathFile(otherIntendedFiles$filename[1])
          if (!is.null(otherIntendedData) && nrow(otherIntendedData) > 0) {
            # Verify the data has x and z columns
            if ("x" %in% colnames(otherIntendedData) && "z" %in% colnames(otherIntendedData)) {
              otherIntendedPaths[[length(otherIntendedPaths) + 1]] <- otherIntendedData
            }
          }
        }
      }
    }
    
    # Debug output
    if (length(otherPatternIds) > 0) {
      cat("  Test", globalTestNum, ": Found", length(otherPatternIds), 
          "other patterns, loaded", length(otherIntendedPaths), "intended paths\n")
    }
    
    # Create overlay plot
    p <- ggplot()
    
    # Add all other patterns' intended paths first (very light/thin to show interference)
    for (otherPath in otherIntendedPaths) {
      p <- p + geom_path(data = otherPath, aes(x = x, y = z), 
                         color = "gray60", linewidth = 0.2, alpha = 0.4, linetype = "dashed")
    }
    
    # Add current intended path (blue, solid, prominent)
    p <- p + geom_path(data = intendedData, aes(x = x, y = z), 
                       color = "blue", linewidth = 0.8, alpha = 0.7, linetype = "solid")
    
    # Add actual path (red, solid, prominent)
    p <- p + geom_path(data = actualData, aes(x = x, y = z), 
                       color = "red", linewidth = 0.8, alpha = 0.7, linetype = "solid") +
      # Title with test info
      labs(
        title = paste0("Test #", globalTestNum, " | ", patId, " | Stage ", stage, 
                       "\nNew: ", ifelse(is.na(newPattern), "N/A", newPattern),
                       " | Recall: ", sprintf("%.1f%%", recallPercent),
                       " | ", ifelse(passed, "[PASS]", "[FAIL]")),
        x = "X",
        y = "Z"
      ) +
      theme_minimal() +
      theme(
        plot.title = element_text(size = 8, face = "bold", hjust = 0.5),
        axis.title = element_text(size = 7),
        axis.text = element_text(size = 6),
        plot.margin = margin(5, 5, 5, 5)
      ) +
      coord_fixed(ratio = 1, xlim = globalXLim, ylim = globalZLim)
    
    allPlots[[length(allPlots) + 1]] <- p
    cat("  Created plot for Test #", globalTestNum, " (", patId, ")\n", sep = "")
  }
  
  # Close null device
  dev.off()
  options(oldOptions)
  
  if (length(allPlots) == 0) {
    stop("No valid plots created.")
  }
  
  cat("\n=== Creating Combined Grid Visualization ===\n")
  cat("Total plots to combine:", length(allPlots), "\n")
  
  # Calculate grid dimensions
  nPlots <- length(allPlots)
  nCols <- ceiling(sqrt(nPlots * 1.5))  # Slightly wider than tall
  nRows <- ceiling(nPlots / nCols)
  
  cat("Grid layout:", nRows, "rows ×", nCols, "columns\n")
  
  # Create combined plot for display
  combinedPlot <- do.call(grid.arrange, c(allPlots, ncol = nCols))
  
  # Save plot
  if (savePlot) {
    rgraphsDir <- file.path(folder, "rgraphs")
    if (!dir.exists(rgraphsDir)) {
      dir.create(rgraphsDir, recursive = TRUE)
    }
    
    pdfFile <- file.path(rgraphsDir, "combined_recall_overlays.pdf")
    
    # Use arrangeGrob for saving
    combinedGrob <- do.call(arrangeGrob, c(allPlots, ncol = nCols))
    ggsave(pdfFile, plot = combinedGrob, width = nCols * 3, height = nRows * 3, 
           units = "in", limitsize = FALSE)
    cat("\n[SUCCESS] Saved combined plot to:", pdfFile, "\n")
    cat("  Size:", nCols * 3, "×", nRows * 3, "inches\n")
  }
  
  cat("\n=== Summary ===\n")
  cat("Total recall tests visualized:", length(allPlots), "\n")
  
  return(list(combinedPlot = combinedPlot, allPlots = allPlots, recallHistory = recallHistory))
}
