import { Component, type ErrorInfo, type ReactNode } from 'react'
import { Button, Empty } from '../components/ui'

interface Props {
  children: ReactNode
  onBack?: () => void
}

interface State {
  error: Error | null
}

/** Keeps one broken screen from taking down the other panes. Remount (key) to reset. */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Pane crashed', error, info.componentStack)
  }

  render() {
    if (!this.state.error) return this.props.children
    return (
      <Empty title="This screen crashed" message={this.state.error.message}>
        <Button variant="primary" onClick={() => this.setState({ error: null })}>
          Try again
        </Button>
        {this.props.onBack && <Button onClick={this.props.onBack}>Go back</Button>}
      </Empty>
    )
  }
}
