# Minimal test script to isolate the stack overflow issue
library(ggplot2)
library(dplyr)

# Simple test function
testFunction <- function(folder) {
    cat("Testing with folder:", folder, "\n")
    historyFile <- file.path(folder, "recall_history.csv")
    if (file.exists(historyFile)) {
        cat("File exists, size:", file.info(historyFile)$size, "bytes\n")
        data <- read.csv(historyFile, stringsAsFactors = FALSE)
        cat("Loaded", nrow(data), "rows\n")
        return(data)
    } else {
        cat("File not found\n")
        return(NULL)
    }
}

# Test it
folder <- "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/_seed226"
result <- testFunction(folder)
