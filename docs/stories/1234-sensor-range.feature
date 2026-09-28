Feature: Out-of-range sensor reading detection
  As a flight operations engineer
  I want every sensor reading classified against its calibrated limits
  So that anomalies are surfaced before they affect mission decisions

  # Scenarios tagged @in-process inject faults, move the clock, or inspect the store, so they run
  # only against the in-memory host. The staging run filters them out by tag.
  #
  # Scenarios that also run against staging stay well clear of time boundaries: the test agent's
  # clock and the network delay are not the server's (ADR 0006). Readings whose time is not under
  # test are stamped 30 seconds in the past.

  Background:
    Given sensor "TMP-07" has limits -40.0 to 125.0 degrees C
    And readings may be at most 5 minutes old and 2 seconds ahead of server time

  @AC-1
  Scenario Outline: Limits are inclusive
    When a reading of <value> is received
    Then the reading is classified "<classification>"

    Examples:
      | value      | classification |
      | -40.0      | Nominal        |
      | 125.0      | Nominal        |
      | -40.000001 | Low            |
      | 125.000001 | High           |

  @AC-2 @in-process
  Scenario: Non-finite reading is rejected, not classified
    When a reading of NaN is received
    Then the reading is rejected with error "NonFiniteValue"
    And no classification event is published

  @AC-3 @in-process
  Scenario: Duplicate reading is idempotent
    Given a reading for "TMP-07" was accepted
    When a reading with the same sensor and timestamp arrives again
    Then no second event is published

  @AC-4 @in-process
  Scenario: Event store is unavailable
    Given the event store is not reachable
    When a valid reading is received
    Then the API returns 503 with a Retry-After header
    And the reading is not partially persisted

  @AC-5 @in-process
  Scenario Outline: Freshness boundaries are exact
    When a reading observed <offset> is received
    Then the reading is rejected with error "<error>"

    Examples:
      | offset                  | error           |
      | 5 minutes 1 second ago  | StaleReading    |
      | 3 seconds in the future | FutureTimestamp |

  @AC-5
  Scenario Outline: Readings far outside the freshness window are rejected
    When a reading observed <offset> is received
    Then the reading is rejected with error "<error>"

    Examples:
      | offset                   | error           |
      | 10 minutes 0 seconds ago | StaleReading    |
      | 60 seconds in the future | FutureTimestamp |

  @AC-6
  Scenario: A reading without a value is rejected
    When a reading without a value is received
    Then the request is rejected as invalid, naming "value"

  @AC-7
  Scenario: A timestamp without a UTC offset is rejected
    When a reading stamped without a UTC offset is received
    Then the request is rejected as invalid, naming "observedAt"

  @AC-8
  Scenario: A different value for a stored timestamp is a conflict
    Given a reading for "TMP-07" was accepted
    When a different value with the same sensor and timestamp arrives
    Then the reading is rejected with status 409 and code "ConflictingReading"

  @AC-9 @in-process
  Scenario: The event store does not answer within the dependency budget
    Given the event store does not answer
    When a valid reading is received and the dependency budget runs out
    Then the API returns 503 with a Retry-After header
    And the error code is "DependencyTimeout"
    And the reading is not partially persisted

  @AC-10
  Scenario: An older reading arrives after a newer one
    Given a reading for "TMP-07" was accepted
    When a reading observed 1 minute before it is received
    Then the reading is classified "Nominal"

  @AC-11
  Scenario: A non-finite reading outside the window is rejected for its value
    When a NaN reading observed 10 minutes ago is received
    Then the reading is rejected with error "NonFiniteValue"

  @AC-12
  Scenario: A reading for an unregistered sensor is rejected
    When a reading for sensor "NOPE-01" is received
    Then the reading is rejected with status 404 and code "SensorNotFound"

  @AC-13 @in-process
  Scenario: An accepted reading is audited once, and its repeat is not
    Given a reading for "TMP-07" was accepted
    When a reading with the same sensor and timestamp arrives again
    Then exactly one audit record names the caller and the reading
